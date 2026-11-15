using System.Collections;
using System.IO;
using UnityEngine;

namespace FieldShift
{
    /// <summary>Saves a screenshot of the Game view, UI included (used by the editor's UI test).</summary>
    public class ShotTaker : MonoBehaviour
    {
        public bool busy;

        public void Take(string path)
        {
            busy = true;
            StartCoroutine(Capture(path));
        }

        IEnumerator Capture(string path)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[FieldShift] UI shot " + path);
            busy = false;
        }
    }
}
