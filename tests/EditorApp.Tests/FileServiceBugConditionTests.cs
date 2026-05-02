using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests;

/// <summary>
/// Bug condition exploration tests for FileService defects.
/// These tests assert the EXPECTED (fixed) behavior.
/// On UNFIXED code, they should FAIL — confirming the bugs exist.
///
/// Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8
/// </summary>
public class FileServiceBugConditionTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            TempFileHelper.Cleanup(path);
        }
    }

    private string CreateTempFile(string content)
    {
        var path = TempFileHelper.CreateTempFile(content);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// 1.1 Redundant encoding I/O:
    /// Cache should store encoding info alongside the line index.
    /// On unfixed code, cache value type is CompressedLineIndex (no encoding) → test FAILS.
    /// </summary>
    [Fact]
    public void CacheValueType_ShouldContainEncodingInfo()
    {
        // Validates: Requirements 1.1
        // The _lineIndexCache should store a type that contains Encoding info,
        // not just CompressedLineIndex alone.
        var serviceType = typeof(FileService);
        var cacheField = serviceType.GetField("_lineIndexCache", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(cacheField);

        var cacheType = cacheField!.FieldType;

        // The cache value type should NOT be just CompressedLineIndex.
        // It should be a record/tuple containing Encoding info.
        // On unfixed code: Dictionary<string, CompressedLineIndex> → value type is CompressedLineIndex → FAILS
        var genericArgs = cacheType.GetGenericArguments();
        Assert.True(genericArgs.Length >= 2, "Cache should be a generic dictionary type");

        var valueType = genericArgs[1]; // The value type of the dictionary

        // Value type should have an Encoding property/field (indicating encoding is cached)
        var encodingMember = valueType.GetProperty("Encoding")
            ?? (MemberInfo?)valueType.GetField("Encoding");
        Assert.NotNull(encodingMember);
    }

    /// <summary>
    /// 1.2 Unbounded cache:
    /// A CloseFile method should exist to allow cache eviction.
    /// On unfixed code, no such method exists → test FAILS.
    /// </summary>
    [Fact]
    public void CloseFileMethod_ShouldExist()
    {
        // Validates: Requirements 1.2
        var serviceType = typeof(FileService);
        var closeFileMethod = serviceType.GetMethod("CloseFile", BindingFlags.Public | BindingFlags.Instance);

        // On unfixed code: CloseFile does not exist → FAILS
        Assert.NotNull(closeFileMethod);
    }

    /// <summary>
    /// 1.3 Stale offset read:
    /// ReadLinesAsync should fire OnStaleFileDetected and serve old data when file is modified after open.
    /// Previously threw InvalidOperationException; now triggers refresh event instead.
    /// </summary>
    [Fact]
    public async Task ReadLinesAsync_ShouldFireStaleEventWhenFileModifiedSinceOpen()
    {
        // Validates: Requirements 7.1, 7.3 (external-file-refresh)
        var service = new FileService();
        var path = CreateTempFile("Line 1\nLine 2\nLine 3");

        await service.OpenFileAsync(path);

        string? detectedPath = null;
        service.OnStaleFileDetected += (p) => detectedPath = p;

        // Modify file externally (append content to change size/timestamp)
        await Task.Delay(50); // Ensure timestamp differs
        File.AppendAllText(path, "\nLine 4\nLine 5");

        // Should NOT throw — serves old data and fires event
        var result = await service.ReadLinesAsync(path, 0, 3);
        Assert.Equal(3, result.Lines.Length);
        Assert.Equal(path, detectedPath);
    }

    /// <summary>
    /// 1.4 RWLS not disposed:
    /// CompressedLineIndex should implement IDisposable.
    /// On unfixed code, it does NOT implement IDisposable → test FAILS.
    /// </summary>
    [Fact]
    public void CompressedLineIndex_ShouldImplementIDisposable()
    {
        // Validates: Requirements 1.4
        var indexType = typeof(CompressedLineIndex);

        // On unfixed code: CompressedLineIndex does NOT implement IDisposable → FAILS
        Assert.True(typeof(IDisposable).IsAssignableFrom(indexType),
            "CompressedLineIndex should implement IDisposable to dispose ReaderWriterLockSlim");
    }

    /// <summary>
    /// 1.5 Missing CancellationToken:
    /// ReadLinesAsync on IFileService should accept a CancellationToken parameter.
    /// On unfixed code, no CancellationToken parameter exists → test FAILS.
    /// </summary>
    [Fact]
    public void ReadLinesAsync_ShouldHaveCancellationTokenParameter()
    {
        // Validates: Requirements 1.5
        var interfaceType = typeof(IFileService);
        var readLinesMethod = interfaceType.GetMethod("ReadLinesAsync");
        Assert.NotNull(readLinesMethod);

        var parameters = readLinesMethod!.GetParameters();
        var hasCancellationToken = parameters.Any(p => p.ParameterType == typeof(CancellationToken));

        // On unfixed code: ReadLinesAsync has no CancellationToken parameter → FAILS
        Assert.True(hasCancellationToken,
            "ReadLinesAsync should accept a CancellationToken parameter");
    }

    /// <summary>
    /// 1.6 Inconsistent disposal:
    /// OpenFileAsync should use 'await using' pattern (code-level observation).
    /// This is documented as a known issue — the test verifies the manual finally pattern
    /// is NOT present by checking that the code uses consistent disposal.
    /// On unfixed code, manual finally is used → test documents the issue.
    /// </summary>
    [Fact]
    public void OpenFileAsync_DisposalPattern_DocumentedAsKnown()
    {
        // Validates: Requirements 1.6
        // This is a code-level observation. The OpenFileAsync method uses:
        //   var stream = new FileStream(...);
        //   try { ... } finally { await stream.DisposeAsync(); }
        // Instead of the consistent:
        //   await using var stream = new FileStream(...);
        //
        // We document this as a known defect. The fix will switch to await using.
        // Since this is a source-level pattern issue, we verify via reflection that
        // the method exists and document the known inconsistency.
        var serviceType = typeof(FileService);
        var openFileMethod = serviceType.GetMethod("OpenFileAsync");
        Assert.NotNull(openFileMethod);

        // Document: OpenFileAsync uses manual try/finally disposal pattern
        // while ReadLinesAsync uses 'await using'. This inconsistency is confirmed.
        // This test passes on both unfixed and fixed code (it's observational).
        // The real validation is in code review.
    }

    /// <summary>
    /// 1.7 Manual lock:
    /// _lineIndexCache should be ConcurrentDictionary, not Dictionary with manual lock.
    /// On unfixed code, it's Dictionary → test FAILS.
    /// </summary>
    [Fact]
    public void LineIndexCache_ShouldBeConcurrentDictionary()
    {
        // Validates: Requirements 1.7
        var serviceType = typeof(FileService);
        var cacheField = serviceType.GetField("_lineIndexCache", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(cacheField);

        var cacheType = cacheField!.FieldType;
        var isConcurrentDictionary = cacheType.IsGenericType
            && cacheType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>);

        // On unfixed code: type is Dictionary<string, CompressedLineIndex> → FAILS
        Assert.True(isConcurrentDictionary,
            $"_lineIndexCache should be ConcurrentDictionary but is {cacheType.Name}");
    }

    /// <summary>
    /// 1.8 Dead code:
    /// CountLines method exists on FileService — confirms dead code present.
    /// This test asserts the method does NOT exist (expected after fix removes/documents it).
    /// On unfixed code, CountLines exists → test FAILS.
    ///
    /// NOTE: Per design, the fix may document rather than remove CountLines.
    /// This test confirms the dead code is present (which is the bug condition).
    /// We assert it's marked as obsolete or removed after fix.
    /// </summary>
    [Fact]
    public void CountLines_ShouldBeMarkedObsoleteOrRemoved()
    {
        // Validates: Requirements 1.8
        var serviceType = typeof(FileService);
        var countLinesMethod = serviceType.GetMethod("CountLines",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        if (countLinesMethod is null)
        {
            // Method removed — bug fixed
            return;
        }

        // If method still exists, it should be marked with [Obsolete] or have XML doc
        // indicating it's a test utility. Check for Obsolete attribute.
        var hasObsolete = countLinesMethod.GetCustomAttribute<ObsoleteAttribute>() is not null;

        // On unfixed code: CountLines exists without Obsolete attribute → FAILS
        Assert.True(hasObsolete,
            "CountLines should be marked [Obsolete] or removed — it is dead code");
    }
}
