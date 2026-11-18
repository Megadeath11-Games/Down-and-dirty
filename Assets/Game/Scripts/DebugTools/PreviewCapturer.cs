using System.Collections;
using System.IO;
using UnityEngine;

namespace FPSBase.DebugTools
{
    /// <summary>Lets the scene run for a few seconds, then renders the main camera from a set viewpoint to a PNG.</summary>
    public class PreviewCapturer : MonoBehaviour
    {
        public string outputPath = "Screenshots/preview.png";
        public Vector3 viewPosition = new Vector3(18f, 0.6f, -40f);
        public float viewYaw = -10f;
        public float viewPitch = 4f;
        public int width = 1600, height = 900;

        IEnumerator Start()
        {
            var cam = Camera.main;
            var player = cam.GetComponentInParent<FPSBase.Player.SourceMovement>();
            if (player != null)
            {
                player.GetComponent<FPSBase.Player.PlayerLook>().enabled = false;
                player.Teleport(viewPosition);
                player.transform.rotation = Quaternion.Euler(0f, viewYaw, 0f);
                cam.transform.parent.localPosition = new Vector3(0f, 1.63f, 0f);
                cam.transform.parent.localRotation = Quaternion.Euler(viewPitch, 0f, 0f);
            }

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            for (int i = 0; i < 240; i++) yield return null; // let exposure / sky / TAA settle
            // WaitForEndOfFrame never fires in batch mode, so render explicitly once more before reading back.
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllBytes(outputPath, tex.EncodeToPNG());
        }
    }
}
