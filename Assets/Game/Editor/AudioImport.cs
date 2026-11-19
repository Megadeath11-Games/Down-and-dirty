using UnityEditor;
using UnityEngine;

namespace FPSBase.EditorTools
{
    /// <summary>Music streams from disk (long tracks would take a lot of memory unpacked); effects load small and ready.</summary>
    public class AudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/Resources/Audio/")) return;
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            if (assetPath.Contains("/Music/"))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                ai.loadInBackground = true;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.85f;
            }
            ai.defaultSampleSettings = s;
        }
    }
}
