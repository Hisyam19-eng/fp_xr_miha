using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace XRFixIt.Editor
{
    /// <summary>
    /// Ensures the generated AndroidManifest declares PICO OS VR app metadata and hand-tracking permission.
    /// </summary>
    public class PicoAndroidManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);

            XmlElement manifestNode = doc.SelectSingleNode("/manifest") as XmlElement;
            XmlElement applicationNode = doc.SelectSingleNode("/manifest/application") as XmlElement;
            if (manifestNode == null || applicationNode == null)
            {
                return;
            }

            EnsureElement(doc, manifestNode, "uses-permission", "com.picovr.permission.HAND_TRACKING", null);
            EnsureElement(doc, applicationNode, "meta-data", "pvr.app.type", "vr");

            doc.Save(manifestPath);
        }

        private static void EnsureElement(XmlDocument doc, XmlElement parent, string tagName, string androidName, string androidValue)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child is XmlElement existing &&
                    existing.Name == tagName &&
                    existing.GetAttribute("name", AndroidNs) == androidName)
                {
                    if (androidValue != null)
                    {
                        existing.SetAttribute("value", AndroidNs, androidValue);
                    }
                    return;
                }
            }

            XmlElement element = doc.CreateElement(tagName);
            element.SetAttribute("name", AndroidNs, androidName);
            if (androidValue != null)
            {
                element.SetAttribute("value", AndroidNs, androidValue);
            }
            parent.AppendChild(element);
        }
    }
}
