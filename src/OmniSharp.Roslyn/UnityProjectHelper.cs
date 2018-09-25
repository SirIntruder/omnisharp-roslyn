using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

public static class UnityProjectHelper
{
	public const string AssemblyCSharp = "Assembly-CSharp";
	public const string AssemblyCSharpEditor = "Assembly-CSharp-Editor";
	public const string AssemblyCSharpFirstpass = "Assembly-CSharp-firstpass";
	public const string AssemblyCSharpEditorFirstpass = "Assembly-CSharp-Editor-firstpass";

	public static Project GetProjectForFilePath(IEnumerable<Project> projects, string filePath)
	{
		foreach(var candidateName in GetOrderedCandidates())
		{
			var unityProject = projects.FirstOrDefault(p => p.Name == candidateName);
			if (unityProject != null) { return unityProject; }
		}
		return null;

		IEnumerable<string> GetOrderedCandidates()
		{
			var isFirstPass = IsFirstPass(filePath);
			var isEditor = IsEditor(filePath);
			if (isFirstPass && isEditor)
			{
				return new [] { AssemblyCSharpEditorFirstpass, AssemblyCSharpFirstpass, AssemblyCSharpEditor, AssemblyCSharp };
			}
			else if (isFirstPass)
			{
				return new [] { AssemblyCSharpFirstpass, AssemblyCSharpEditorFirstpass, AssemblyCSharpEditor, AssemblyCSharp };
			}
			else if (isEditor)
			{
				return new [] { AssemblyCSharpEditor, AssemblyCSharp, AssemblyCSharpEditorFirstpass, AssemblyCSharpFirstpass };
			}
			else
			{
				return new [] { AssemblyCSharp, AssemblyCSharpEditor, AssemblyCSharpEditorFirstpass, AssemblyCSharpFirstpass };
			}
		}
	}

	public static bool IsFirstPass(string filePath)
	{
		return filePath.StartsWith("Assets/Plugins", StringComparison.Ordinal) ||
			filePath.StartsWith("Assets/Standard Assets", StringComparison.Ordinal) ||
			filePath.StartsWith("Assets/Pro Standard Assets", StringComparison.Ordinal);
	}

	public static bool IsEditor(string filePath)
	{
		return filePath.Contains("/Editor/");
	}
}