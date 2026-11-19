using System.Collections.Generic;
using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSBase.Vehicles
{
    /// <summary>Shared vehicle logic: get in/out with E, fuel tank, own camera, HUD text.</summary>
    public abstract class VehicleBase : Interactable, IHudProvider
    {
        [Header("Vehicle")]
        public string displayName = "Vehicle";
        public float fuelCapacity = 60f;      // liters
        public float fuel = 60f;
        public float idleBurn = 1.5f;         // liters per hour at idle
        public float workBurn = 8f;           // liters per hour at full work
        [Tooltip("Game hours of fuel burn per real second. 1/600 = 10 real minutes per engine hour.")]
        public float timeScaleHours = 1f / 600f;

        public Transform exitPoint;
        public VehicleCamera vehicleCamera;

        static readonly List<VehicleBase> all = new();
        /// <summary>Every machine in the world (for the map, recall and the compass).</summary>
        public static IReadOnlyList<VehicleBase> All => all;
        /// <summary>The machine the player drove last.</summary>
        public static VehicleBase LastDriven { get; private set; }

        protected virtual void OnEnable() => all.Add(this);
        protected virtual void OnDisable() => all.Remove(this);

        EngineAudio engine;

        public bool Occupied { get; private set; }
        public bool HasFuel => fuel > 0f;
        public float FuelFraction => fuel / Mathf.Max(fuelCapacity, 0.01f);

        protected PlayerInteractor driver;
        int enteredFrame;
        protected float workAmount; // 0..1 set by subclasses each frame for fuel burn

        public override string Prompt => NetRemoteDriven ? $"{displayName}  —  {NetDriverName} is driving it"
                                                         : $"Get in {displayName}   (fuel {fuel:0}/{fuelCapacity:0} L)";

        // ------------------------------------------------------------------ multiplayer

        /// <summary>Another player's game runs this machine (they drive it, or drove it last): this copy only shows it.</summary>
        public bool NetRemote { get; private set; }
        /// <summary>Another player is sitting in it right now.</summary>
        public bool NetRemoteDriven { get; set; }
        public string NetDriverName { get; set; } = "your friend";
        /// <summary>Forward speed the owner reported (wheels and tracks of a remote machine turn at this).</summary>
        public float NetSpeed { get; set; }
        /// <summary>Same on every player's game: which machine this is.</summary>
        public int NetId { get; set; }
        /// <summary>Chained down on a trailer deck: it moves with the trailer, its own wheels and tracks don't turn.</summary>
        public bool RidingTrailer { get; set; }
        float netWork;
        RigidbodyInterpolation netInterp = RigidbodyInterpolation.Interpolate;
        /// <summary>The local player got in / out (the network session claims the machine).</summary>
        public static System.Action<VehicleBase> NetEntered, NetExited;
        /// <summary>Dirt poured into a machine another player runs goes to their game (set by the network session).</summary>
        public static System.Action<VehicleBase, MaterialLoad> NetForwardLoad;

        public void SetNetRemote(bool remote)
        {
            if (remote == NetRemote) return;
            if (remote && Occupied) Exit();
            NetRemote = remote;
            if (body == null) body = GetComponent<Rigidbody>();
            if (body != null)
            {
                if (remote) netInterp = body.interpolation;
                body.isKinematic = remote;
                body.interpolation = remote ? RigidbodyInterpolation.None : netInterp;
                if (!remote) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            }
            if (!remote) { NetRemoteDriven = false; NetSpeed = 0f; }
            hasPose = false;
        }

        /// <summary>This machine's state for the other players (the owner sends it ~15 times a second).</summary>
        public virtual void NetWrite(System.IO.BinaryWriter w)
        {
            w.Write(fuel); w.Write(workAmount);
            NetIO.Write(w, cab);
        }

        public virtual void NetRead(System.IO.BinaryReader r)
        {
            fuel = r.ReadSingle(); netWork = r.ReadSingle();
            cab = NetIO.ReadCab(r);
        }

        /// <summary>Remote copy, every frame: move the arms / blade / box toward the last state received.</summary>
        protected virtual void NetShow(float dt) { }

        public override void Interact(PlayerInteractor player)
        {
            if (Occupied || NetRemoteDriven) return;
            Trailer.ReleaseCargo(this); // chained down on a lowboy: the chains come off
            if (NetRemote) SetNetRemote(false); // take it over: this game runs it from now on
            NetEntered?.Invoke(this);
            enteredBuried = Buried;
            driver = player;
            Occupied = true;
            enteredFrame = Time.frameCount;
            LastDriven = this;
            var det = player.GetComponent<FPSBase.Player.MetalDetector>();
            if (det != null && det.Out) det.SetOut(false);
            // the driver's body climbs in: it sits in the seat, visible through the cab glass
            var body = player.GetComponent<FPSBase.Player.PlayerBody>();
            Transform eye = vehicleCamera != null && vehicleCamera.seat != null ? vehicleCamera.seat : FindCabView();
            if (body != null && eye != null)
            {
                var cam = vehicleCamera;
                body.SitIn(eye, () => cam != null && cam.isActiveAndEnabled && cam.CabView);
            }
            player.SetActive(false);
            if (vehicleCamera != null) vehicleCamera.gameObject.SetActive(true);
            if (GameUI.Instance != null) GameUI.Instance.ActiveVehicle = this;
            OnEnterVehicle();
            if (enteredBuried)
            {
                var rbody = GetComponent<Rigidbody>();
                if (BuryDepth >= hullHeight * 0.4f)
                {
                    if (rbody != null) rbody.isKinematic = true;
                    GameUI.Toast($"{displayName} is buried ({BuryDepth:0.0} m deep): dig it out to drive it");
                }
                else Unbury();
            }
        }

        // ------------------------------------------------------------------ burying

        /// <summary>
        /// Dirt piled up all round it (backfilled in a pit, or buried on purpose): the machine stays where it is
        /// instead of the ground floating it up, until it's dug out. A machine buried deep can't drive; an
        /// excavator can still work its arm (dig itself out).
        /// </summary>
        public bool Buried { get; private set; }
        /// <summary>How deep the dirt round the machine is above the bottom of its tracks / wheels (m).</summary>
        public float BuryDepth { get; private set; }
        public string BuryDebug => $"hull {hullMin}..{hullMax} bottomY {hullMinY:0.00} kin {(body != null && body.isKinematic)}";
        float buryTimer, hullMinY, hullHeight;
        Vector3 hullMin, hullMax;
        bool hullKnown, buryWasKinematic;

        void FindHull()
        {
            hullKnown = true;
            bool any = false;
            Vector3 mn = Vector3.zero, mx = Vector3.zero;
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                if (r.GetComponentInParent<CabControls>() != null) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = transform.InverseTransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    if (!any) { mn = mx = c; any = true; } else { mn = Vector3.Min(mn, c); mx = Vector3.Max(mx, c); }
                }
            }
            if (!any) { mn = new Vector3(-1f, 0f, -1.5f); mx = new Vector3(1f, 2f, 1.5f); }
            hullMin = mn; hullMax = mx;
            hullHeight = Mathf.Max(0.5f, mx.y - mn.y);
            // the bottom of the tracks / wheels: the lowest point near the middle of the machine (not a bucket down a hole)
            hullMinY = mn.y;
            if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<VehicleBase>() != this)
                hullMinY = Mathf.Max(mn.y, transform.InverseTransformPoint(hit.point).y - 0.05f);
        }

        void BuryCheck()
        {
            var site = DigSite.At(transform.position);
            if (site == null || !site.Contains(transform.position)) { if (Buried) Unbury(); return; }
            if (!hullKnown) FindHull();
            // sample the ground just outside the machine's footprint, all the way round
            float sum = 0f; int deep = 0, n = 0;
            Vector3 lo = hullMin - new Vector3(0.3f, 0f, 0.3f), hi = hullMax + new Vector3(0.3f, 0f, 0.3f);
            for (int i = 0; i < 12; i++)
            {
                float t = i / 12f * 4f;
                int side = (int)t; float f = t - side;
                float x = side switch { 0 => Mathf.Lerp(lo.x, hi.x, f), 1 => hi.x, 2 => Mathf.Lerp(hi.x, lo.x, f), _ => lo.x };
                float z = side switch { 0 => lo.z, 1 => Mathf.Lerp(lo.z, hi.z, f), 2 => hi.z, _ => Mathf.Lerp(hi.z, lo.z, f) };
                Vector3 p = transform.TransformPoint(new Vector3(x, hullMinY, z));
                if (!site.Contains(p)) continue;
                float d = site.SurfaceY(p) - p.y;
                sum += Mathf.Max(0f, d); n++;
                if (d > 0.15f) deep++;
            }
            BuryDepth = n > 0 ? sum / n : 0f;
            bool surrounded = n > 0 && deep >= n * 0.65f;
            if (body == null) body = GetComponent<Rigidbody>();
            if (!Buried)
            {
                bool still = body == null || body.isKinematic || body.linearVelocity.sqrMagnitude < 0.25f;
                if (!Occupied && !DriveInput && still && surrounded && BuryDepth > BuryAt) Bury();
            }
            else
            {
                // dug out, or (with someone in it) only shallow enough to drive out of
                if (BuryDepth < BuryAt * 0.5f || (Occupied && BuryDepth < hullHeight * 0.4f)) Unbury();
                else if (body != null && !body.isKinematic)
                {
                    // something released it (a script, the test, a respawn): when it's dug out, it stays a physics body
                    buryWasKinematic = false;
                    body.isKinematic = true;
                }
            }
        }

        float BuryAt => Mathf.Max(0.3f, hullHeight * 0.15f);
        /// <summary>Being driven (or test-driven): a machine pushing into a berm isn't buried.</summary>
        protected virtual bool DriveInput => false;
        /// <summary>The driver is on the throttle (a trailer's brakes let go when it pulls away).</summary>
        public bool Throttling => DriveInput;
        /// <summary>The driver's brake pedal for a trailer's brake controller: 1 on, 0 off, -1 this vehicle doesn't say (the trailer goes by how hard it slows).</summary>
        public virtual int BrakePedal => -1;

        void Bury()
        {
            Buried = true;
            if (body == null) return;
            buryWasKinematic = body.isKinematic;
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = true;
        }

        void Unbury()
        {
            if (!Buried) return;
            Buried = false;
            // (it's held kinematic while buried: if it isn't now, something released it, and it stays released)
            if (body != null) body.isKinematic = NetRemote || (!Occupied && buryWasKinematic && body.isKinematic);
            if (Occupied) GameUI.Toast($"{displayName} is dug out");
        }

        /// <summary>The driver's eye point in the cab.</summary>
        public Transform CabEye => vehicleCamera != null && vehicleCamera.seat != null ? vehicleCamera.seat : FindCabView();

        Transform FindCabView()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == "CabView") return t;
            return null;
        }

        public void Exit()
        {
            if (!Occupied) return;
            Occupied = false;
            NetExited?.Invoke(this);
            OnExitVehicle();
            if (vehicleCamera != null) vehicleCamera.gameObject.SetActive(false);
            if (GameUI.Instance != null && ReferenceEquals(GameUI.Instance.ActiveVehicle, this)) GameUI.Instance.ActiveVehicle = null;

            Vector3 p = exitPoint != null ? exitPoint.position : transform.position + transform.right * -2f;
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point + Vector3.up * 0.05f;
            driver.SetActive(true);
            driver.PlaceAt(p, transform.eulerAngles.y);
            var body = driver.GetComponent<FPSBase.Player.PlayerBody>();
            if (body != null) body.StandUp();
            driver = null;
        }

        // ------------------------------------------------------------------ look-at menu: refuel, reposition, tow

        public override void GetOptions(List<InteractOption> list)
        {
            bool friend = NetRemoteDriven;
            list.Add(new InteractOption
            {
                label = friend ? $"{NetDriverName} is driving it" : $"Get in   (fuel {fuel:0}/{fuelCapacity:0} L)",
                enabled = !friend, note = friend ? "a friend is in it" : null, run = Interact,
            });
            var tank = FuelTank.Near(this);
            if (tank != null)
            {
                float need = fuelCapacity - fuel;
                bool can = need >= 0.5f && tank.liters >= 0.5f && !friend;
                list.Add(new InteractOption
                {
                    label = $"Refuel from the {tank.label.ToLower()}   ({tank.liters:0}/{tank.capacity:0} L in it)",
                    enabled = can,
                    note = friend ? "a friend is in it" : need < 0.5f ? "tank's full" : "the tank is empty: fill it at the fuel pump",
                    run = p =>
                    {
                        float l = tank.FillMachine(this);
                        GameUI.Toast($"Pumped {l:0} L from the {tank.label.ToLower()} into {displayName}  ({tank.liters:0} L left in it)");
                    },
                });
            }
            foreach (var t in GetComponentsInChildren<FuelTank>())
                list.Add(FPSBase.Mining.FuelPump.FillOption(t, t.transform.position));
            ExtraOptions(list);
            Trailer.AddTruckOptions(this, list);
            list.Add(new InteractOption
            {
                label = "Reposition   (unstick it / set it upright nearby)",
                enabled = !friend && !Occupied && !(Buried && BuryDepth > hullHeight * 0.4f),
                note = friend ? "a friend is in it" : "it's buried: dig it out first",
                run = p => Reposition(),
            });
            list.Add(new InteractOption
            {
                label = "Call a tow truck   (pick a spot on the map, you ride along)",
                enabled = !friend && !Occupied && !Buried,
                note = friend ? "a friend is in it" : "it's buried: dig it out first",
                run = p => GameUI.Instance.OpenTow(this),
            });
        }

        /// <summary>Machine-specific look-at choices (a pickup's tidy tank).</summary>
        protected virtual void ExtraOptions(List<InteractOption> list) { }

        /// <summary>Bounds of the machine in its own space (footprint and height).</summary>
        public Bounds LocalBounds
        {
            get
            {
                if (!localBoundsKnown) { localBounds = VehicleSpot.LocalBounds(transform); localBoundsKnown = true; }
                return localBounds;
            }
        }
        Bounds localBounds; bool localBoundsKnown;

        /// <summary>Tow price: a call-out fee, plus distance and the machine's weight.</summary>
        public float TowCost(Vector3 dest)
        {
            if (body == null) body = GetComponent<Rigidbody>();
            float km = Vector3.Distance(transform.position, dest) / 1000f;
            float tons = body != null ? body.mass / 1000f : 5f;
            return Mathf.Round(150f + km * 900f + tons * 6f);
        }

        /// <summary>Unstick: set it upright on clear, flat ground as close as possible to where it is.</summary>
        public void Reposition()
        {
            if (Occupied) return;
            TakeOver();
            float yaw = Heading();
            Vector3 at = VehicleSpot.FindFree(transform.position, yaw, LocalBounds, transform, 25f);
            Place(at, yaw);
            GameUI.Toast($"{displayName} repositioned");
        }

        /// <summary>The way it points on the map, even rolled on its side or roof (eulerAngles.y can swing round then).</summary>
        float Heading()
        {
            Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            return f.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(f).eulerAngles.y : transform.eulerAngles.y;
        }

        /// <summary>A tow truck takes it to `dest` (paid); the player rides along and gets dropped next to it.</summary>
        public bool TowTo(Vector3 dest, PlayerInteractor player)
        {
            if (Occupied || Buried) return false;
            var eco = Economy.Instance;
            float cost = TowCost(dest);
            if (eco != null && !eco.TrySpend(cost)) return false;
            TakeOver();
            float yaw = Heading();
            Vector3 at = VehicleSpot.FindFree(dest, yaw, LocalBounds, transform, 40f);
            Place(at, yaw);
            if (player != null)
            {
                // you climb out of the tow truck beside it
                var lb = LocalBounds;
                Vector3 side = transform.right * (lb.extents.x + 1.5f) + transform.TransformDirection(new Vector3(lb.center.x, 0f, lb.center.z));
                Vector3 me = FPSBase.Mining.VehicleDealer.GroundAt(transform.position + side) + Vector3.up * 0.1f;
                player.PlaceAt(me, Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.position - me, Vector3.up).normalized + Vector3.forward * 1e-4f).eulerAngles.y);
            }
            GameUI.Toast(eco != null && eco.Unlimited ? $"{displayName} towed (free in test mode)" : $"{displayName} towed for ${cost:N0}");
            return true;
        }

        /// <summary>A friend's game ran it last: this game takes it over before moving it, so it sticks.</summary>
        void TakeOver()
        {
            if (!NetRemote) return;
            SetNetRemote(false);
            NetEntered?.Invoke(this);
            NetExited?.Invoke(this);
        }

        void Place(Vector3 ground, float yaw)
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (Buried) Unbury();
            Trailer.ReleaseCargo(this);
            var hooked = Trailer.Hooked(this);
            transform.SetPositionAndRotation(ground + Vector3.up * 0.6f, Quaternion.Euler(0f, yaw, 0f));
            if (body != null)
            {
                body.position = transform.position; body.rotation = transform.rotation;
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            }
            Trailer.Follow(this, hooked);
            Physics.SyncTransforms();
        }

        /// <summary>Tipped over or stuck: lift the machine a little and set it back on its tracks/wheels.</summary>
        public void Recover()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            Vector3 p = transform.position;
            float y = FPSBase.Mining.VehicleDealer.GroundAt(p).y;
            transform.SetPositionAndRotation(new Vector3(p.x, Mathf.Max(p.y, y) + 1.2f, p.z), Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            if (body != null && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            Physics.SyncTransforms();
            GameUI.Toast($"{displayName} set back upright");
        }

        /// <summary>Bring an empty machine to a spot (map "Recall").</summary>
        public bool RecallTo(Vector3 spot, float yaw)
        {
            if (Occupied) return false;
            if (body == null) body = GetComponent<Rigidbody>();
            float y = FPSBase.Mining.VehicleDealer.GroundAt(spot).y;
            transform.SetPositionAndRotation(new Vector3(spot.x, y + 0.8f, spot.z), Quaternion.Euler(0f, yaw, 0f));
            if (body != null && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            Physics.SyncTransforms();
            return true;
        }

        public void Refuel(float liters) => fuel = Mathf.Min(fuelCapacity, fuel + liters);

        /// <summary>What the operator is doing with the cab controls this frame (set by DriveUpdate).</summary>
        protected CabInput cab;
        public CabInput CabState => cab;
        CabControls cabControls;
        bool cabLooked;
        public CabControls Controls3D
        {
            get
            {
                if (!cabLooked) { cabControls = GetComponentInChildren<CabControls>(true); cabLooked = true; }
                return cabControls;
            }
        }

        protected virtual void Update()
        {
            if (NetRemote)
            {
                workAmount = netWork;
                if (Controls3D != null) Controls3D.Set(cab);
                NetShow(Time.deltaTime);
                if (engine == null) engine = EngineAudio.Attach(this);
                engine.Drive(NetRemoteDriven && fuel > 0f, workAmount, Mathf.Abs(NetSpeed));
                return;
            }
            if ((buryTimer -= Time.deltaTime) <= 0f) { buryTimer = 0.3f; BuryCheck(); }
            workAmount = 0f;
            cab = default;
            bool blocked = GameUI.Instance != null && GameUI.Instance.BlocksGameplay;
            if (Occupied && !blocked)
            {
                if (Controls.Pressed(GameAction.Interact) && Time.frameCount > enteredFrame) { Exit(); return; }
                if (Controls.Pressed(GameAction.Recover)) Recover();
                DriveUpdate(Time.deltaTime);
            }
            else IdleUpdate(Time.deltaTime);
            if (Controls3D != null) Controls3D.Set(cab);

            if ((trampleTimer -= Time.deltaTime) <= 0f) { trampleTimer = 0.1f; TrampleGround(); }
            if (engine == null) engine = EngineAudio.Attach(this);
            if (body == null) body = GetComponent<Rigidbody>();
            engine.Drive(Occupied && fuel > 0f, workAmount, body != null && !body.isKinematic ? body.linearVelocity.magnitude : 0f);

            if (Occupied && fuel > 0f)
            {
                float lph = Mathf.Lerp(idleBurn, workBurn, Mathf.Clamp01(workAmount));
                fuel = Mathf.Max(0f, fuel - lph * timeScaleHours * Time.deltaTime);
                if (fuel <= 0f) GameUI.Toast($"{displayName} is out of fuel! Buy fuel at the fuel pump.");
            }
        }

        float trampleTimer, lastTrample;
        Rigidbody body;
        Vector3 lastPos;
        Quaternion lastRot;
        bool hasPose;
        TrackAnimator tracks;
        bool tracksLooked;

        /// <summary>
        /// Wheels/tracks scuff the grass off the dirt field, rut loose dirt and press into the ground while the
        /// machine moves. Each contact's own movement counts, so a tracked machine turning on the spot (which
        /// barely moves its body) still scrubs both track ends round.
        /// </summary>
        void TrampleGround()
        {
            var site = DigSite.At(transform.position);
            if (site == null) return;
            if (body == null) body = GetComponent<Rigidbody>();
            if (body == null || body.isKinematic) { hasPose = false; return; }
            float dt = Time.time - lastTrample;
            lastTrample = Time.time;
            Vector3 prevPos = lastPos; Quaternion prevRot = lastRot;
            bool had = hasPose && dt > 0.01f && dt < 0.5f;
            lastPos = transform.position; lastRot = transform.rotation; hasPose = true;
            if (!had) return;
            if ((transform.position - prevPos).sqrMagnitude > 9f) return; // recovered / teleported
            int nc = 0;
            foreach (var _ in ContactPoints()) nc++;
            float perContact = body.mass / Mathf.Max(1, nc);
            float tonnes = body.mass / 1000f;
            Quaternion inv = Quaternion.Inverse(transform.rotation);
            foreach (var p in ContactPoints())
            {
                Vector3 before = prevPos + prevRot * (inv * (p - transform.position));
                Vector3 v = (p - before) / dt;
                v.y = 0f;
                if (v.sqrMagnitude < 0.04f) continue;
                site.Trample(p, ContactRadius);
                site.Rut(p, ContactRadius, perContact, v);
                site.Compact(p, ContactRadius * 1.1f, tonnes);
                site.TrackPress(p, ContactRadius, tonnes, transform.forward, v);
            }
        }

        TrackAnimator Tracks
        {
            get
            {
                if (!tracksLooked) { tracks = GetComponent<TrackAnimator>(); tracksLooked = true; }
                return tracks;
            }
        }

        protected virtual float ContactRadius => Tracks != null ? Mathf.Max(0.2f, Tracks.ShoeWidth * 0.6f) : 0.35f;

        /// <summary>Where the machine touches the ground: along the bottom of each track, else four points under the main box collider.</summary>
        protected virtual IEnumerable<Vector3> ContactPoints()
        {
            if (Tracks != null)
            {
                foreach (var p in Tracks.GroundPoints()) yield return p;
                yield break;
            }
            var box = GetComponent<BoxCollider>();
            if (box == null) yield break;
            Vector3 c = box.center, s = box.size;
            float y = c.y - s.y * 0.5f;
            for (int i = 0; i < 4; i++)
                yield return transform.TransformPoint(new Vector3((i % 2 == 0 ? -0.36f : 0.36f) * s.x, y, (i < 2 ? -0.38f : 0.38f) * s.z));
        }

        protected virtual void OnEnterVehicle() { }
        bool enteredBuried;
        protected virtual void OnExitVehicle() { }
        protected abstract void DriveUpdate(float dt);
        protected virtual void IdleUpdate(float dt) { }

        public string HudTitle => displayName;
        public virtual string HudBody => $"Fuel {fuel:0.0} / {fuelCapacity:0} L";
        public abstract string HudControls { get; }
        public virtual float HudBar => -1f;
        public virtual string HudBarLabel => "";
    }
}
