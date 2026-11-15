using UnityEditor;

namespace FieldShift.EditorTools
{
    /// <summary>Import settings for the CC0 terrain textures in Resources/FieldShift/Textures.</summary>
    public class TextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter;
            if (assetPath.Contains("/Resources/FieldShift/Sky/"))
            {
                ti.textureShape = TextureImporterShape.Texture2D;
                ti.maxTextureSize = 4096;
                ti.mipmapEnabled = false;                 // avoids a seam where the panorama wraps
                ti.wrapModeU = UnityEngine.TextureWrapMode.Repeat; ti.wrapModeV = UnityEngine.TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            if (!assetPath.Contains("/Resources/FieldShift/Textures/")) return;
            ti.maxTextureSize = 1024;
            ti.mipmapEnabled = true;
            ti.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            string file = System.IO.Path.GetFileName(assetPath);
            if (assetPath.EndsWith("_Normal.jpg")) ti.textureType = TextureImporterType.NormalMap;
            else if (file.StartsWith("Foliage_"))
            {
                // alpha-tested cards: keep their density in the distance
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = 0.42f;
                ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                ti.isReadable = false;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.isReadable = false;          // nothing reads these on the CPU (compressed web textures can't be read back)
                ti.sRGBTexture = true;
            }
        }
    }
}
