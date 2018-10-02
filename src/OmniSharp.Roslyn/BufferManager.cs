using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using OmniSharp.FileWatching;
using OmniSharp.Models;
using OmniSharp.Models.ChangeBuffer;
using OmniSharp.Models.UpdateBuffer;

namespace OmniSharp.Roslyn
{
    public class BufferManager
    {
        private readonly OmniSharpWorkspace _workspace;
        private readonly IDictionary<string, IEnumerable<DocumentId>> _transientDocuments = new Dictionary<string, IEnumerable<DocumentId>>(StringComparer.OrdinalIgnoreCase);
        private readonly ISet<DocumentId> _transientDocumentIds = new HashSet<DocumentId>();
        private readonly object _lock = new object();
        private readonly IFileSystemWatcher _fileSystemWatcher;
        private readonly Action<string, FileChangeType> _onFileChanged;

        public BufferManager(OmniSharpWorkspace workspace, IFileSystemWatcher fileSystemWatcher)
        {
            _workspace = workspace;
            _workspace.WorkspaceChanged += OnWorkspaceChanged;
            _fileSystemWatcher = fileSystemWatcher;
            _onFileChanged = OnFileChanged;
        }

        public async Task UpdateBufferAsync(Request request)
        {
            var buffer = request.Buffer;
            var changes = request.Changes;

            if (request is UpdateBufferRequest updateRequest && updateRequest.FromDisk)
            {
                buffer = File.ReadAllText(updateRequest.FileName);
            }

            if (request.FileName == null || (buffer == null && changes == null))
            {
                return;
            }

            var solution = _workspace.CurrentSolution;

            var documentIds = solution.GetDocumentIdsWithFilePath(request.FileName);
            if (!documentIds.IsEmpty)
            {
                if (changes == null)
                {
                    var sourceText = SourceText.From(buffer);

                    foreach (var documentId in documentIds)
                    {
                        solution = solution.WithDocumentText(documentId, sourceText);
                    }
                }
                else
                {
                    foreach (var documentId in documentIds)
                    {
                        var document = solution.GetDocument(documentId);
                        var sourceText = await document.GetTextAsync();

                        foreach (var change in request.Changes)
                        {
                            var startOffset = sourceText.Lines.GetPosition(new LinePosition(change.StartLine, change.StartColumn));
                            var endOffset = sourceText.Lines.GetPosition(new LinePosition(change.EndLine, change.EndColumn));

                            sourceText = sourceText.WithChanges(new[] {
                                new TextChange(new TextSpan(startOffset, endOffset - startOffset), change.NewText)
                            });
                        }

                        solution = solution.WithDocumentText(documentId, sourceText);
                    }
                }

                _workspace.TryApplyChanges(solution);
            }
            else if (buffer != null)
            {
                TryAddTransientDocument(request.FileName, buffer);
            }
        }

        public async Task UpdateBufferAsync(ChangeBufferRequest request)
        {
            if (request.FileName == null)
            {
                return;
            }

            var solution = _workspace.CurrentSolution;

            var documentIds = solution.GetDocumentIdsWithFilePath(request.FileName);
            if (!documentIds.IsEmpty)
            {
                foreach (var documentId in documentIds)
                {
                    var document = solution.GetDocument(documentId);
                    var sourceText = await document.GetTextAsync();

                    var startOffset = sourceText.Lines.GetPosition(new LinePosition(request.StartLine, request.StartColumn));
                    var endOffset = sourceText.Lines.GetPosition(new LinePosition(request.EndLine, request.EndColumn));

                    sourceText = sourceText.WithChanges(new[] {
                        new TextChange(new TextSpan(startOffset, endOffset - startOffset), request.NewText)
                    });

                    solution = solution.WithDocumentText(documentId, sourceText);
                }

                _workspace.TryApplyChanges(solution);
            }
            else
            {
                // TODO@joh ensure the edit is an insert at offset zero
                TryAddTransientDocument(request.FileName, request.NewText);
            }
        }

        private bool TryAddTransientDocument(string fileName, string fileContent)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            var projects = FindProjectsByFileName(fileName);
            if (!projects.Any())
            {
                if (fileName.EndsWith(".cs") && _workspace.TryAddMiscellaneousDocument(fileName, LanguageNames.CSharp) != null)
                {
                    _fileSystemWatcher.Watch(fileName, OnFileChanged);
                    return true;
                }

                return false;
            }
            else
            {
                var sourceText = SourceText.From(fileContent);
                var documentInfos = new List<DocumentInfo>();
                foreach (var project in projects)
                {
                    var id = DocumentId.CreateNewId(project.Id);
                    var version = VersionStamp.Create();
                    var documentInfo = DocumentInfo.Create(
                        id, fileName, filePath: fileName,
                        loader: TextLoader.From(TextAndVersion.Create(sourceText, version)));

                    documentInfos.Add(documentInfo);
                }

                lock (_lock)
                {
                    var documentIds = documentInfos.Select(document => document.Id);
                    _transientDocuments.Add(fileName, documentIds);
                    _transientDocumentIds.UnionWith(documentIds);
                }

                foreach (var documentInfo in documentInfos)
                {
                    _workspace.AddDocument(documentInfo);
                }
            }

            return true;
        }

        private void OnFileChanged(string filePath, FileChangeType changeType)
        {
            if (changeType == FileChangeType.Unspecified && !File.Exists(filePath) || changeType == FileChangeType.Delete)
            {
                _workspace.TryRemoveMiscellaneousDocument(filePath);
            }
        }

        private IEnumerable<Project> FindProjectsByFileName(string fileName)
        {
            var fileInfo = new FileInfo(fileName);
            var dirInfo = fileInfo.Directory;
            var candidates = _workspace.CurrentSolution.Projects
                .Where(project => !String.IsNullOrWhiteSpace (project.FilePath))
                .GroupBy(project => new FileInfo(project.FilePath).Directory.FullName)
                .ToDictionary(grouping => grouping.Key, grouping => grouping.ToList());

            List<Project> projects = null;
            while (dirInfo != null)
            {
                if (candidates.TryGetValue(dirInfo.FullName, out projects))
                {
                    // check if found projects match Unity3D standard projects - if so, emulate Unity's algorithm.
                    var unityProject = UnityProjectHelper.GetProjectForFilePath(projects, fileName);
                    if (unityProject != null)
                    {
                        return new [] { unityProject };
                    }

                    return projects;
                }

                dirInfo = dirInfo.Parent;
            }

            return Array.Empty<Project>();
        }

        private void OnWorkspaceChanged(object sender, WorkspaceChangeEventArgs args)
        {
            string fileName = null;
            if (args.Kind == WorkspaceChangeKind.DocumentAdded)
            {
                fileName = args.NewSolution.GetDocument(args.DocumentId).FilePath;
            }
            else if (args.Kind == WorkspaceChangeKind.DocumentRemoved)
            {
                fileName = args.OldSolution.GetDocument(args.DocumentId).FilePath;
            }

            if (fileName == null)
            {
                return;
            }

            lock (_lock)
            {
                bool replacedWithRealDocument =
                    args.Kind == WorkspaceChangeKind.DocumentAdded && !_transientDocumentIds.Contains(args.DocumentId);
                bool documentRemoved =
                    args.Kind == WorkspaceChangeKind.DocumentRemoved;

                if (replacedWithRealDocument || documentRemoved)
                {
                    if (!_transientDocuments.TryGetValue(fileName, out var documentIds))
                    {
                        return;
                    }

                    _transientDocuments.Remove(fileName);
                    foreach (var documentId in documentIds)
                    {
                        _transientDocumentIds.Remove(documentId);

                        // remove transient document from workspace if not already.
                        if (!documentRemoved)
                        {
                            _workspace.RemoveDocument(documentId);
                        }
                    }
                }
            }
        }
    }
}
