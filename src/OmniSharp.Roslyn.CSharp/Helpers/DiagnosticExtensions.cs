﻿using Microsoft.CodeAnalysis;
using OmniSharp.Models.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OmniSharp.Helpers
{
    internal static class DiagnosticExtensions
    {
        internal static DiagnosticLocation ToDiagnosticLocation(this Diagnostic diagnostic)
        {
            var span = diagnostic.Location.GetMappedLineSpan();
            return new DiagnosticLocation
            {
                FileName = span.Path,
                Line = span.StartLinePosition.Line,
                Column = span.StartLinePosition.Character,
                EndLine = span.EndLinePosition.Line,
                EndColumn = span.EndLinePosition.Character,
                Text = $"{diagnostic.GetMessage()} ({diagnostic.Id})",
                LogLevel = diagnostic.Severity.ToString(),
                Id = diagnostic.Id
            };
        }

        internal static IEnumerable<DiagnosticLocation> DistinctDiagnosticLocationsByProject(this IEnumerable<(string projectName, Diagnostic diagnostic)> analyzerResults)
        {
            return analyzerResults
                .Select(x => new
                {
                    location = x.diagnostic.ToDiagnosticLocation(),
                    project = x.projectName
                })
                .GroupBy(x => x.location)
                .Select(x =>
                {
                    var location = x.First().location;
                    location.Projects = x.Select(a => a.project).ToList();
                    return location;
                });
        }
    }
}
