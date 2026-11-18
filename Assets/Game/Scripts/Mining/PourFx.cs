using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>Falling dirt clumps when pouring and spraying bits when digging. One shared particle system.</summary>
    public static class PourFx
    {
        static ParticleSystem ps;
        static float carry;

        static ParticleSystem System
        {
            get
            {
                if (ps != null) return ps;
                var go = new GameObject("DirtParticles");
                ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = false;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = 2.5f;
                main.startSpeed = 0f;
                main.gravityModifier = 1f;
                main.maxParticles = 4000;
                main.startRotation3D = true;
                var emission = ps.emission; emission.enabled = false;
                var shape = ps.shape; shape.enabled = false;
                var col = ps.collision;
                col.enabled = true; col.type = ParticleSystemCollisionType.World; col.mode = ParticleSystemCollisionMode.Collision3D;
                col.lifetimeLoss = 0.6f; col.dampen = 0.8f; col.bounce = 0.1f; col.quality = ParticleSystemCollisionQuality.Medium;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Mesh;
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                r.mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(tmp);
                if (DigSite.Instance != null && DigSite.Instance.pileMaterial != null) r.sharedMaterial = DigSite.Instance.pileMaterial;
                ps.Play();
                return ps;
            }
        }

        /// <summary>Emit clumps for `volume` m^3 falling from `from`.</summary>
        public static void Stream(Vector3 from, Vector3 ground, float volume)
        {
            carry += volume * 1500f; // clumps per m^3
            int n = Mathf.Min((int)carry, 40);
            if (n <= 0) return;
            carry -= n;
            var p = System;
            for (int i = 0; i < n; i++)
            {
                var e = new ParticleSystem.EmitParams
                {
                    position = from + Random.insideUnitSphere * 0.15f,
                    velocity = Random.insideUnitSphere * 0.4f + Vector3.down * 0.5f,
                    startSize = Random.Range(0.04f, 0.11f),
                    rotation3D = Random.insideUnitSphere * 180f,
                };
                p.Emit(e, 1);
            }
        }

        /// <summary>A few bits kicked up while the bucket digs.</summary>
        public static void Dig(Vector3 at)
        {
            // clods kicked up by the teeth
            for (int k = 0; k < 2; k++)
                DirtGrains.Spray(at + Vector3.up * 0.1f + Random.insideUnitSphere * 0.15f, Random.insideUnitSphere * 1.3f + Vector3.up * 1.8f, 0, Random.Range(0.05f, 0.1f));
            if (Random.value > 0.35f) return;
            var p = System;
            var e = new ParticleSystem.EmitParams
            {
                position = at + Vector3.up * 0.1f,
                velocity = Random.insideUnitSphere * 1.2f + Vector3.up * 1.5f,
                startSize = Random.Range(0.03f, 0.08f),
                rotation3D = Random.insideUnitSphere * 180f,
            };
            p.Emit(e, 1);
        }
    }
}
