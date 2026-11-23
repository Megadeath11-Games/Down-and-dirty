using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// A truck's, trailer's or machine's body scraping the dirt as it goes (a bumper clipping the far side of a dip,
    /// a belly dragging over a hump, a trailer's skirt on a heap): the dirt it hits is shoved aside in front of it,
    /// within reason, instead of the ground being a wall that stops it dead. Loose dirt goes easily; it only shaves
    /// a few cm off unbroken ground (more for a heavier machine), so a real bank still stops it.
    /// </summary>
    public static class BodyDirt
    {
        /// <summary>Off: bodies hit the dirt like a wall (for the tests' comparison).</summary>
        public static bool On = true;
        /// <summary>Dirt shoved aside by bodies so far (m³, for the tests).</summary>
        public static float Shoved;
        /// <summary>Body contacts with the dirt seen so far (for the tests).</summary>
        public static int Seen;

        static readonly ContactPoint[] buf = new ContactPoint[24];
        // how much unbroken ground each body may still shave (m³): it refills slowly, so a scrape goes through but
        // driving into a bank doesn't tunnel through it
        static readonly System.Collections.Generic.Dictionary<Rigidbody, (float left, float at)> allowance = new();

        public static void Contact(Rigidbody rb, Collision c)
        {
            if (rb == null || rb.isKinematic || c == null || !DigSite.IsSiteCollider(c.collider)) return;
            Seen += c.contactCount;
            if (!On) return;
            var site = DigSite.At(rb.position);
            if (site == null) return;
            int n = c.GetContacts(buf);
            if (n == 0) return;
            float tonnes = rb.mass / 1000f;
            float cap = tonnes * 0.03f, now = Time.time;
            float left = allowance.TryGetValue(rb, out var a) ? Mathf.Min(cap, a.left + (now - a.at) * tonnes * 0.02f) : cap;
            // how much unbroken ground a pass shaves (m): a pickup a couple of cm, a laden semi or a big hauler a hand
            float nativeCut = Mathf.Clamp(0.012f * tonnes, 0.025f, 0.18f);
            float width = Mathf.Clamp(0.35f + tonnes * 0.01f, 0.4f, 0.9f);
            // (a handful of the contacts each step is plenty: they're all along the same scrape)
            int step = Mathf.Max(1, n / 4);
            bool clods = DigSite.PushClods;
            DigSite.PushClods = false; // (straight into the squares ahead: no clods flying off every scrape)
            try
            {
                for (int i = 0; i < n; i++)
                {
                    if (i % step != 0) continue;
                    var p = buf[i];
                    if (p.thisCollider == null || p.thisCollider.isTrigger) continue;
                    // (a truck's solid tyre: the wheel does the ground's work, not the body)
                    if (p.thisCollider.name == "Tyre Support") continue;
                    Vector3 v = rb.GetPointVelocity(p.point); v.y = 0f;
                    float sp = v.magnitude;
                    if (sp < 0.3f) continue;
                    Vector3 dir = v / sp;
                    // resting on it and barely moving (a belly on a hump, crawling): only when it's really going
                    float sideways = Vector3.ProjectOnPlane(p.normal, Vector3.up).magnitude;
                    if (sideways < 0.3f && sp < 1.2f) continue;
                    // everything the body is in, down to just under where it touches
                    float bottom = p.point.y + Mathf.Min(0f, p.separation) - 0.04f;
                    float moved = site.Spread(p.point, dir, width, bottom, 0.45f + sp * 0.05f, left > 0f ? nativeCut : 0f, out float native);
                    left -= native;
                    Shoved += moved;
                }
            }
            finally { DigSite.PushClods = clods; allowance[rb] = (left, now); }
            if (allowance.Count > 200) allowance.Clear();
        }
    }
}
