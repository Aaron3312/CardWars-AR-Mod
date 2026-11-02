using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

// Assigns the AR loaders in XR Plug-in Management: ARCore on Android, ARKit on iOS.
// Standalone targets get no loader, so the desktop builds don't start XR.
// Usage: Unity -batchmode -quit -projectPath . -executeMethod ARSetup.Configure
public static class ARSetup
{
	private const string SettingsFolder = "Assets/XR";

	[MenuItem("Card Wars/AR/Configure XR loaders")]
	public static void Configure()
	{
		XRGeneralSettingsPerBuildTarget perTarget = GetOrCreatePerTargetSettings();
		AssignLoader(perTarget, BuildTargetGroup.Android, "UnityEngine.XR.ARCore.ARCoreLoader");
		AssignLoader(perTarget, BuildTargetGroup.iOS, "UnityEngine.XR.ARKit.ARKitLoader");
		AssetDatabase.SaveAssets();
		Debug.Log("ARSetup: XR loaders configured (Android: ARCore, iOS: ARKit)");
	}

	private static XRGeneralSettingsPerBuildTarget GetOrCreatePerTargetSettings()
	{
		EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
		if (perTarget == null)
		{
			if (!AssetDatabase.IsValidFolder(SettingsFolder))
			{
				AssetDatabase.CreateFolder("Assets", "XR");
			}
			perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
			AssetDatabase.CreateAsset(perTarget, SettingsFolder + "/XRGeneralSettingsPerBuildTarget.asset");
			EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
		}
		return perTarget;
	}

	private static void AssignLoader(XRGeneralSettingsPerBuildTarget perTarget, BuildTargetGroup group, string loaderType)
	{
		if (!perTarget.HasSettingsForBuildTarget(group))
		{
			perTarget.CreateDefaultSettingsForBuildTarget(group);
		}
		if (!perTarget.HasManagerSettingsForBuildTarget(group))
		{
			perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
		}
		XRGeneralSettings settings = perTarget.SettingsForBuildTarget(group);
		if (!XRPackageMetadataStore.IsLoaderAssigned(loaderType, group))
		{
			XRPackageMetadataStore.AssignLoader(settings.Manager, loaderType, group);
		}
		EditorUtility.SetDirty(settings);
	}
}
