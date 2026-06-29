// Copyright (c) marcschier. Licensed under the MIT License.

namespace Pgm.Tests;

public sealed class PgmLibraryTests
{
    [Test]
    public async Task Rfc_Is_3208()
    {
        await Assert.That(PgmLibrary.Rfc).IsEqualTo("RFC 3208");
    }
}
