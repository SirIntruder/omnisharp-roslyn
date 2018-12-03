using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

public static class UnityProjectHelper
{
	public static Project GetProjectForFilePath(IEnumerable<Project> projects, string filePath)
	{
		return GetOrderedCandidates(filePath).
			Select(candidate => projects.FirstOrDefault(p => p.Name == candidate)).
			Where(x => x != null).
			FirstOrDefault();
	}

	/// <summary>
	/// Ideal project is maybe not created yet - try to put files in closest existing match.
	/// </summary>
	static IEnumerable<string> GetOrderedCandidates(string filePath)
	{
		var isFirstPass = IsFirstPass(filePath);
		var isEditor = IsEditor(filePath);

		if (isFirstPass && isEditor)
		{
			yield return "Assembly-CSharp-Editor-firstpass";
			yield return "Assembly-CSharp-firstpass";
			yield return "Assembly-CSharp-Editor";
			yield return "Assembly-CSharp";
		}
		else if (isFirstPass)
		{
			yield return "Assembly-CSharp-firstpass";
			yield return "Assembly-CSharp-Editor-firstpass";
			yield return "Assembly-CSharp";
			yield return "Assembly-CSharp-Editor";
		}
		else if (isEditor)
		{
			yield return "Assembly-CSharp-Editor";
			yield return "Assembly-CSharp";
			yield return "Assembly-CSharp-Editor-firstpass";
			yield return "Assembly-CSharp-firstpass";
		}
		else
		{
			yield return "Assembly-CSharp";
			yield return "Assembly-CSharp-Editor";
			yield return "Assembly-CSharp-Editor-firstpass";
			yield return "Assembly-CSharp-firstpass";
		}
	}

	public static bool IsFirstPass(string filePath)
	{
		return filePath.Contains("Assets/Plugins") || filePath.Contains("Assets\\Plugins") ||
			filePath.Contains("Assets/Standard Assets") || filePath.Contains("Assets\\Standard Assets") ||
			filePath.Contains("Assets/Pro Standard Assets") || filePath.Contains("Assets\\Pro Standard Assets");
	}

	public static bool IsEditor(string filePath)
	{
		return filePath.Contains("/Editor/") || filePath.Contains("\\Editor\\");
	}
}