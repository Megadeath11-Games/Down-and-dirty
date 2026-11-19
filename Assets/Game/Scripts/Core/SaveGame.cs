using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FPSBase.Mining;
using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>
    /// Saved worlds: one file per world in the game's save folder, tagged single player or multiplayer. A save holds
    /// the worked ground (every cell dug, filled or rutted), every machine and trailer (the ones that came with the
    /// claim and the ones bought since: where they are, fuel, tidy tank, what's in the dump box, a lowboy's
    /// gooseneck on or off, a bowser's fuel and what it's hitched to), the dirt piles, your money and gold, and where
    /// you stand. In a multiplayer world the host saves it and hosts it again from the main menu.
    /// </summary>
    public static class SaveGame
    {
        public enum Mode : byte { SinglePlayer = 0, Multiplayer = 1 }

        public class Info
        {
            public string path, name;
            public Mode mode;
            public DateTime saved;
            public float money;
        }

        const int Magic = 0x44445356; // "DDSV"
        const int Version = 1;

        public static string Folder => Path.Combine(Application.persistentDataPath, "Saves");
        /// <summary>The world being played (loaded or last saved): saving again overwrites it.</summary>
        public static string CurrentName { get; set; }
        public static Mode CurrentMode { get; set; }

        static string PathFor(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(Folder, name.Trim() + ".ddsave");
        }

        public static List<Info> List(Mode mode)
        {
            var list = new List<Info>();
            if (!Directory.Exists(Folder)) return list;
            foreach (var f in Directory.GetFiles(Folder, "*.ddsave"))
            {
                try
                {
                    using var r = new BinaryReader(File.OpenRead(f));
                    var info = ReadHeader(r, f);
                    if (info != null && info.mode == mode) list.Add(info);
                }
                catch (Exception e) { Debug.LogWarning($"Unreadable save {f}: {e.Message}"); }
            }
            list.Sort((a, b) => b.saved.CompareTo(a.saved));
            return list;
        }

        static Info ReadHeader(BinaryReader r, string path)
        {
            if (r.ReadInt32() != Magic) return null;
            int ver = r.ReadInt32();
            if (ver > Version) return null;
            return new Info { path = path, mode = (Mode)r.ReadByte(), name = r.ReadString(), saved = new DateTime(r.ReadInt64()), money = r.ReadSingle() };
        }

        public static void Delete(Info i) { try { File.Delete(i.path); } catch (Exception e) { Debug.LogWarning(e.Message); } }

        // ------------------------------------------------------------------ save

        /// <summary>Write the world to the save called `name`. Returns null, or what went wrong.</summary>
        public static string Save(string name, Mode mode)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Give the world a name";
            try
            {
                Directory.CreateDirectory(Folder);
                string path = PathFor(name), tmp = path + ".tmp";
                using (var w = new BinaryWriter(File.Create(tmp)))
                {
                    var eco = Economy.Instance;
                    w.Write(Magic); w.Write(Version); w.Write((byte)mode); w.Write(name.Trim()); w.Write(DateTime.Now.Ticks);
                    w.Write(eco != null ? eco.Money : 0f);
                    w.Write(eco != null ? eco.GoldGrams : 0f);
                    WritePlayer(w);
                    WriteGround(w);
                    WriteMachines(w);
                    WritePiles(w);
                    WritePlants(w);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                CurrentName = name.Trim(); CurrentMode = mode;
                return null;
            }
            catch (Exception e) { Debug.LogException(e); return e.Message; }
        }

        static void WritePlayer(BinaryWriter w)
        {
            Vector3 p = Vector3.zero; float yaw = 0f; bool have = false;
            var ui = GameUI.Instance;
            if (ui != null && ui.ActiveVehicle is VehicleBase av)
            {
                // sitting in a machine: you start next to it
                p = av.exitPoint != null ? av.exitPoint.position : av.transform.position - av.transform.right * 3f;
                yaw = av.transform.eulerAngles.y; have = true;
            }
            else if (ui != null && ui.player != null) { p = ui.player.transform.position; yaw = ui.player.transform.eulerAngles.y; have = true; }
            w.Write(have); V3(w, p); w.Write(yaw);
        }

        static void WriteGround(BinaryWriter w)
        {
            var site = DigSite.Instance;
            var cells = site != null ? site.TakeChangedCells(true) : new List<int>();
            var raw = new MemoryStream(cells.Count * DigSite.NetCellBytes + 16);
            var rw = new BinaryWriter(raw);
            foreach (int i in cells) site.WriteCell(rw, i);
            rw.Flush();
            var packed = new MemoryStream();
            using (var z = new DeflateStream(packed, System.IO.Compression.CompressionLevel.Optimal, true)) z.Write(raw.GetBuffer(), 0, (int)raw.Length);
            w.Write(cells.Count); w.Write((int)packed.Length); w.Write(packed.GetBuffer(), 0, (int)packed.Length);
        }

        static void WriteMachines(BinaryWriter w)
        {
            Net.NetSession.AssignSceneIds();
            var machines = new List<VehicleBase>();
            foreach (var v in VehicleBase.All)
                if (v != null && (v.GetComponent<BoughtFrom>() != null || (v.NetId > 0 && v.NetId < Net.NetSession.SpawnIdBase))) machines.Add(v);
            w.Write(machines.Count);
            foreach (var v in machines)
            {
                var bf = v.GetComponent<BoughtFrom>();
                w.Write(bf == null);
                w.Write(bf == null ? v.NetId : bf.listing);
                w.Write(v.name);
                V3(w, v.transform.position); Q(w, v.transform.rotation);
                w.Write(v.fuel);
                var tanks = v.GetComponentsInChildren<FuelTank>(true);
                w.Write(tanks.Length);
                foreach (var t in tanks) w.Write(t.liters);
                if (v is DumpPickup pk)
                {
                    w.Write((byte)3); // 3 = tidy tank, gooseneck ball, bed load (1 = older saves: no ball)
                    w.Write(pk.TidyTankFitted);
                    w.Write(pk.GooseneckFitted);
                    NetIO.Write(w, pk.load);
                }
                else if (v is SemiTruck semi && Trailer.OwnLowboy(semi) is Trailer low)
                {
                    w.Write((byte)2);
                    w.Write((byte)(!low.NeckOn ? 1 : low.Hitched == null ? 2 : 0)); // 0 hitched, 1 neck off, 2 unhitched
                    V3(w, low.transform.position); Q(w, low.transform.rotation);
                }
                else w.Write((byte)0);
            }
            // trailers bought on their own (fuel bowsers)
            var trailers = new List<Trailer>();
            foreach (var t in Trailer.All) if (t != null && t.GetComponent<BoughtFrom>() != null) trailers.Add(t);
            w.Write(trailers.Count);
            foreach (var t in trailers)
            {
                w.Write(t.GetComponent<BoughtFrom>().listing);
                V3(w, t.transform.position); Q(w, t.transform.rotation);
                w.Write(t.Tank != null ? t.Tank.liters : 0f);
                w.Write(t.Hitched != null && t.Hitched.Vehicle != null ? machines.IndexOf(t.Hitched.Vehicle) : -1);
            }
        }

        static void WritePiles(BinaryWriter w)
        {
            var piles = new List<DirtPile>();
            foreach (var p in DirtPile.All) if (p != null && !p.load.IsEmpty) piles.Add(p);
            w.Write(piles.Count);
            foreach (var p in piles) { V3(w, p.transform.position); NetIO.Write(w, p.load); }
        }

        const int PlantsTag = 0x504C4E54; // "PLNT": the wash plants (upgrades, hopper, sluice gold, fuel). Older saves end before it.

        static void WritePlants(BinaryWriter w)
        {
            var plants = Net.NetSession.Plants();
            w.Write(PlantsTag); w.Write(plants.Count);
            foreach (var p in plants)
            {
                var ms = new MemoryStream(); p.NetWrite(new BinaryWriter(ms));
                w.Write((int)ms.Length); w.Write(ms.GetBuffer(), 0, (int)ms.Length);
            }
        }

        static void ReadPlants(BinaryReader r)
        {
            if (r.BaseStream.Length - r.BaseStream.Position < 8 || r.ReadInt32() != PlantsTag) return;
            var plants = Net.NetSession.Plants();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var bytes = r.ReadBytes(r.ReadInt32());
                if (i < plants.Count) plants[i].NetRead(new BinaryReader(new MemoryStream(bytes)));
            }
        }

        // ------------------------------------------------------------------ load

        /// <summary>
        /// Put a saved world into the running scene (best on a freshly started one: the main menu always is).
        /// Bought machines already in the scene are replaced by the saved ones. `done` gets null, or what went wrong.
        /// </summary>
        public static IEnumerator Load(string path, Action<string> done)
        {
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception e) { done?.Invoke(e.Message); yield break; }
            var r = new BinaryReader(new MemoryStream(data));
            Info info;
            float money, gold, yaw; bool havePlayer; Vector3 playerAt;
            try
            {
                info = ReadHeader(r, path);
                if (info == null) { done?.Invoke("Not a save from this game (or from a newer version)"); yield break; }
                money = info.money; gold = r.ReadSingle();
                havePlayer = r.ReadBoolean(); playerAt = RV3(r); yaw = r.ReadSingle();
                ReadGround(r);
            }
            catch (Exception e) { done?.Invoke("The save is damaged: " + e.Message); yield break; }

            // clear out what was bought in this scene (and the lowboys that came with semis), and the piles
            foreach (var b in UnityEngine.Object.FindObjectsByType<BoughtFrom>(FindObjectsSortMode.None))
            {
                if (b.GetComponent<SemiTruck>() is SemiTruck s && Trailer.OwnLowboy(s) is Trailer lb) UnityEngine.Object.Destroy(lb.gameObject);
                UnityEngine.Object.Destroy(b.gameObject);
            }
            foreach (var p in new List<DirtPile>(DirtPile.All)) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
            yield return null;

            Net.NetSession.AssignSceneIds();
            var dealer = UnityEngine.Object.FindFirstObjectByType<VehicleDealer>();
            var machines = new List<VehicleBase>();
            var lowboys = new List<(SemiTruck semi, byte state, Vector3 p, Quaternion q)>();
            string error = null;
            try
            {
                int n = r.ReadInt32();
                for (int k = 0; k < n; k++)
                {
                    bool scene = r.ReadBoolean();
                    int id = r.ReadInt32();
                    string name = r.ReadString();
                    Vector3 p = RV3(r); Quaternion q = RQ(r);
                    float fuel = r.ReadSingle();
                    int nt = r.ReadInt32();
                    var liters = new float[nt];
                    for (int i = 0; i < nt; i++) liters[i] = r.ReadSingle();
                    byte extra = r.ReadByte();
                    bool tidy = false, goose = false; byte lowState = 0; MaterialLoad bed = default; Vector3 lp = default; Quaternion lq = Quaternion.identity;
                    if (extra == 1) { tidy = r.ReadBoolean(); bed = NetIO.ReadLoad(r); }
                    else if (extra == 3) { tidy = r.ReadBoolean(); goose = r.ReadBoolean(); bed = NetIO.ReadLoad(r); }
                    else if (extra == 2) { lowState = r.ReadByte(); lp = RV3(r); lq = RQ(r); }

                    VehicleBase v = null;
                    if (scene) { foreach (var x in VehicleBase.All) if (x.NetId == id) { v = x; break; } }
                    else if (dealer != null && id >= 0 && id < dealer.listings.Length && dealer.listings[id].prefab != null)
                    {
                        var go = UnityEngine.Object.Instantiate(dealer.listings[id].prefab, p, q);
                        go.name = name;
                        Net.NetSession.NotifySpawn(go, id);
                        v = go.GetComponent<VehicleBase>();
                    }
                    machines.Add(v);
                    if (v == null) continue;
                    Trailer.ReleaseCargo(v);
                    Pose(v.transform, p, q);
                    v.fuel = Mathf.Clamp(fuel, 0f, v.fuelCapacity);
                    var tanks = v.GetComponentsInChildren<FuelTank>(true);
                    for (int i = 0; i < tanks.Length && i < liters.Length; i++) tanks[i].liters = Mathf.Clamp(liters[i], 0f, tanks[i].capacity);
                    if (v is DumpPickup pk && (extra == 1 || extra == 3))
                    {
                        pk.load = default;
                        pk.SetTidyTank(tidy);
                        pk.SetGooseneck(goose);
                        if (!bed.IsEmpty) pk.Receive(bed);
                    }
                    if (v is SemiTruck semi && extra == 2) lowboys.Add((semi, lowState, lp, lq));
                }
            }
            catch (Exception e) { error = "The save is damaged: " + e.Message; }
            if (error != null) { done?.Invoke(error); yield break; }

            // trailers couple up on their first frame: then put the lowboys and the bowsers where they were
            yield return null; yield return null;
            try
            {
                foreach (var (semi, state, p, q) in lowboys)
                {
                    var low = Trailer.OwnLowboy(semi);
                    if (low == null) continue;
                    if (state == 1) low.DetachNeck();
                    else if (state == 2) low.Uncouple();
                    Pose(low.transform, p, q);
                }
                int nt = r.ReadInt32();
                for (int k = 0; k < nt; k++)
                {
                    int listing = r.ReadInt32();
                    Vector3 p = RV3(r); Quaternion q = RQ(r);
                    float liters = r.ReadSingle();
                    int hitchedTo = r.ReadInt32();
                    if (dealer == null || listing < 0 || listing >= dealer.listings.Length || dealer.listings[listing].prefab == null) continue;
                    var go = UnityEngine.Object.Instantiate(dealer.listings[listing].prefab, p, q);
                    Net.NetSession.NotifySpawn(go, listing);
                    var t = go.GetComponent<Trailer>();
                    if (t == null) continue;
                    if (t.Tank != null) t.Tank.liters = Mathf.Clamp(liters, 0f, t.Tank.capacity);
                    if (hitchedTo >= 0 && hitchedTo < machines.Count && machines[hitchedTo] != null)
                        foreach (var h in machines[hitchedTo].GetComponentsInChildren<Hitch>())
                            if (h.type == t.coupler && h.Attached == null) { t.PendingCouple = h; break; }
                }
                int np = r.ReadInt32();
                for (int k = 0; k < np; k++) { Vector3 p = RV3(r); var l = NetIO.ReadLoad(r); DirtPile.DropAt(p, l); }
                ReadPlants(r);
            }
            catch (Exception e) { error = "The save is damaged: " + e.Message; }

            var eco = Economy.Instance;
            if (eco != null) eco.SetState(money, gold);
            var ui = GameUI.Instance;
            if (havePlayer && ui != null && ui.player != null) ui.player.PlaceAt(playerAt + Vector3.up * 0.1f, yaw);
            Physics.SyncTransforms();
            CurrentName = info.name; CurrentMode = info.mode;
            done?.Invoke(error);
        }

        static void ReadGround(BinaryReader r)
        {
            int n = r.ReadInt32(), len = r.ReadInt32();
            var packed = new MemoryStream(r.ReadBytes(len));
            var raw = new MemoryStream();
            using (var z = new DeflateStream(packed, CompressionMode.Decompress)) z.CopyTo(raw);
            raw.Position = 0;
            DigSite.Instance?.ReadCells(new BinaryReader(raw), n);
        }

        static void Pose(Transform t, Vector3 p, Quaternion q)
        {
            t.SetPositionAndRotation(p, q);
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = p; rb.rotation = q;
                if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            Physics.SyncTransforms();
        }

        static void V3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 RV3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        static void Q(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        static Quaternion RQ(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }
}
