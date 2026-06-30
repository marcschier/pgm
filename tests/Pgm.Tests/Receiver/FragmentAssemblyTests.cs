// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Receiver;

namespace Pgm.Tests.Receiver;

public sealed class FragmentAssemblyTests
{
    [Test]
    public async Task TryAdd_FragmentBeyondDeclaredLength_ReturnsFalse()
    {
        var assembly = new FragmentAssembly(4);

        await Assert.That(assembly.TryAdd(2, new byte[] { 1, 2, 3 })).IsFalse();
    }

    [Test]
    public async Task TryAdd_OverlappingFragments_ReturnsFalse()
    {
        var assembly = new FragmentAssembly(6);

        var first = assembly.TryAdd(0, new byte[] { 1, 2, 3 });
        var overlapping = assembly.TryAdd(2, new byte[] { 9, 9 });

        await Assert.That(first).IsTrue();
        await Assert.That(overlapping).IsFalse();
    }

    [Test]
    public async Task TryAdd_DisjointFragments_AssemblesCompletePayload()
    {
        var assembly = new FragmentAssembly(5);

        var first = assembly.TryAdd(0, new byte[] { (byte)'h', (byte)'e' });
        var second = assembly.TryAdd(2, new byte[] { (byte)'l', (byte)'l', (byte)'o' });

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsTrue();
        await Assert.That(assembly.IsComplete).IsTrue();
        await Assert.That(assembly.ToArray()).IsEquivalentTo(new byte[] { 104, 101, 108, 108, 111 });
    }

    [Test]
    public async Task Constructor_LengthExceedingInt32_Throws()
    {
        await Assert.That(() => new FragmentAssembly((uint)int.MaxValue + 1))
            .Throws<ArgumentOutOfRangeException>();
    }
}
