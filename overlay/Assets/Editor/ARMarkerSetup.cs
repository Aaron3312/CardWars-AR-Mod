using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.ARSubsystems;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

// The printed marker of "Versus en la mesa" (docs/ar-versus.md): an image library with one 15 cm image,
// loaded by ARBattleRig from Resources.
// Usage: Unity -batchmode -quit -projectPath . -executeMethod ARMarkerSetup.CreateLibrary
//
// It also builds the library's ARCore database before ARCore's own build step. That step walks every image
// library in the project and stops at the first one arcoreimg rejects (the test library inside the ARKit
// package), so ours could be left without data and never be detected on Android.
public class ARMarkerSetup : IPreprocessBuildWithReport
{
	public const string TexturePath = "Assets/ARBattle/VersusMarker.png";

	public const string LibraryPath = "Assets/Resources/ARBattle/VersusMarkerLibrary.asset";

	public const string ImageName = "VersusMarker";

	public const float SizeMeters = 0.15f;

	private const string ARCoreDataKey = "com.unity.xr.arcore";

	public int callbackOrder
	{
		get
		{
			return 1;
		}
	}

	[MenuItem("Card Wars/AR/Create versus marker library")]
	public static void CreateLibrary()
	{
		AssetDatabase.ImportAsset(TexturePath);
		Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
		if (texture == null)
		{
			UnityEngine.Debug.LogError("ARMarkerSetup: " + TexturePath + " not found");
			return;
		}
		XRReferenceImageLibrary library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(LibraryPath);
		if (library == null)
		{
			library = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
			AssetDatabase.CreateAsset(library, LibraryPath);
		}
		while (library.count > 0)
		{
			library.RemoveAt(0);
		}
		library.Add();
		library.SetTexture(0, texture, false);
		library.SetName(0, ImageName);
		library.SetSpecifySize(0, true);
		library.SetSize(0, new Vector2(SizeMeters, SizeMeters));
		EditorUtility.SetDirty(library);
		AssetDatabase.SaveAssets();
		UnityEngine.Debug.Log("ARMarkerSetup: " + LibraryPath + " has " + ImageName + " at " + SizeMeters + " m");
	}

	public void OnPreprocessBuild(BuildReport report)
	{
		if (report.summary.platform != BuildTarget.Android)
		{
			return;
		}
		XRReferenceImageLibrary library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(LibraryPath);
		if (library == null || library.count == 0)
		{
			UnityEngine.Debug.LogWarning("ARMarkerSetup: no versus marker library; image tracking will find nothing");
			return;
		}
		library.SetDataForKey(ARCoreDataKey, BuildARCoreDatabase(library));
		UnityEngine.Debug.Log("ARMarkerSetup: ARCore image database built for " + LibraryPath);
	}

	private static byte[] BuildARCoreDatabase(XRReferenceImageLibrary library)
	{
		UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.xr.arcore/package.json");
		if (package == null)
		{
			throw new BuildFailedException("ARMarkerSetup: com.unity.xr.arcore is not installed");
		}
		string platform = (Application.platform == RuntimePlatform.OSXEditor) ? "MacOS" : ((Application.platform == RuntimePlatform.WindowsEditor) ? "Windows" : "Linux");
		string tool = Path.Combine(package.resolvedPath, "Tools~", platform, (platform == "Windows") ? "arcoreimg.exe" : "arcoreimg");
		string temp = Path.Combine(Path.GetTempPath(), "cw-marker-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(temp);
		try
		{
			string list = Path.Combine(temp, "images.txt");
			string db = Path.Combine(temp, "marker.imgdb");
			using (StreamWriter writer = new StreamWriter(list))
			{
				for (int i = 0; i < library.count; i++)
				{
					XRReferenceImage image = library[i];
					string source = AssetDatabase.GUIDToAssetPath(image.textureGuid.ToString("N"));
					string copy = Path.Combine(temp, image.guid.ToString("N") + Path.GetExtension(source).ToLowerInvariant());
					File.Copy(source, copy);
					writer.WriteLine(image.guid.ToString("N") + "|" + copy + "|" + image.width.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
				}
			}
			if (platform != "Windows")
			{
				Run("/bin/chmod", "+x \"" + tool + "\"");
			}
			string output = Run(tool, "build-db --input_image_list_path=\"" + list + "\" --output_db_path=\"" + db + "\"");
			if (!File.Exists(db))
			{
				throw new BuildFailedException("ARMarkerSetup: arcoreimg did not build the marker database: " + output);
			}
			return File.ReadAllBytes(db);
		}
		finally
		{
			Directory.Delete(temp, true);
		}
	}

	private static string Run(string file, string arguments)
	{
		System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo(file, arguments);
		info.UseShellExecute = false;
		info.RedirectStandardOutput = true;
		info.RedirectStandardError = true;
		info.CreateNoWindow = true;
		using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info))
		{
			string stdout = process.StandardOutput.ReadToEnd();
			string stderr = process.StandardError.ReadToEnd();
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				throw new BuildFailedException("ARMarkerSetup: " + Path.GetFileName(file) + " exited with " + process.ExitCode + ": " + stderr);
			}
			return stdout + stderr;
		}
	}
}
