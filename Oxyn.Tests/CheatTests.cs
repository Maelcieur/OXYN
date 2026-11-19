using System.IO;
using System.Runtime.InteropServices;
using Oxyn.ConsoleMode.Cheats;
using Xunit;

namespace Oxyn.Tests;

public class CheatTests
{
    [Fact]
    public void Scan_FindsAlignedValuesOfEveryType()
    {
        var buf = new byte[32];
        buf[3] = 7;                                    // octet
        BitConverter.GetBytes((short)7).CopyTo(buf, 8);  // entier 16 bits (et octet 8)
        BitConverter.GetBytes(7).CopyTo(buf, 12);        // entier 32 bits (et 16 bits, et octet 12)
        BitConverter.GetBytes(7f).CopyTo(buf, 20);       // décimal
        var hits = new List<(long Addr, CheatType Type)>();
        CheatValue.Scan(buf, 0x1000, 7, hits);
        Assert.DoesNotContain(hits, h => h.Type == CheatType.Byte);   // octets seuls : trop de résultats
        Assert.Contains((0x1008L, CheatType.Int16), hits);
        Assert.Contains((0x100CL, CheatType.Int32), hits);
        Assert.Contains((0x1014L, CheatType.Float), hits);
        Assert.DoesNotContain((0x1003L, CheatType.Int16), hits);   // pas aligné
    }

    [Fact]
    public void Simplify_KeepsSmallestIntegerTypePerAddress()
    {
        var r = CheatSearch.Simplify(new[] { (16L, CheatType.Byte), (16L, CheatType.Int16), (16L, CheatType.Int32), (24L, CheatType.Float) });
        Assert.Equal(new[] { (16L, CheatType.Byte), (24L, CheatType.Float) }, r);
    }

    [Fact]
    public void Encode_RoundTripsAndClamps()
    {
        Assert.Equal(300, CheatValue.Read(CheatValue.Encode(300, CheatType.Int16), CheatType.Int16));
        Assert.Equal(255, CheatValue.Read(CheatValue.Encode(999, CheatType.Byte), CheatType.Byte));
        Assert.Equal(2.5, CheatValue.Read(CheatValue.Encode(2.5, CheatType.Float), CheatType.Float));
        Assert.Equal("3", CheatValue.Format(3.00001));
        Assert.Equal("2.5", CheatValue.Format(2.5).Replace(',', '.'));
    }

    [Fact]
    public void Search_FindsAndRefinesInARealProcess()
    {
        var data = new int[16];
        data[5] = 913_572_468;
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var addr = (long)pin.AddrOfPinnedObject() + 5 * 4;
            using var mem = new ProcessMemory(Environment.ProcessId);
            using var search = CheatSearch.First(mem, 913_572_468, null, CancellationToken.None);
            Assert.True(search.Count >= 1);
            data[5] = 913_572_469;
            search.Next(mem, 913_572_469, null, CancellationToken.None);
            Assert.Contains(search.Results(1000), r => r.Addr == addr && r.Type == CheatType.Int32);
            search.Next(mem, 913_572_469, null, CancellationToken.None);   // rien ne change : affinage bloqué
            Assert.True(search.Stalled);

            var b = new byte[4];
            Assert.True(mem.Write(addr, CheatValue.Encode(42, CheatType.Int32)));
            Assert.Equal(42, data[5]);
            Assert.True(mem.Read(addr, b));
            Assert.Equal(42, CheatValue.Read(b, CheatType.Int32));
        }
        finally { pin.Free(); }
    }

    [Fact]
    public void Fingerprint_SkipsValueAndScoresSimilarity()
    {
        var window = Enumerable.Range(1, 34).Select(i => (byte)i).ToArray();   // 16 avant + valeur 2 octets + 16 après
        var fp = CheatFingerprint.FromWindow(window, 2);
        Assert.Equal(32, fp.Length);
        Assert.Equal(16, fp[15]);
        Assert.Equal(19, fp[16]);                       // 17 et 18 = la valeur, ignorée
        var changed = (byte[])fp.Clone();
        for (var i = 0; i < 6; i++) changed[i] ^= 0xFF;   // 6 voisins ont bougé : encore reconnu
        Assert.True(CheatFingerprint.Matches(fp, changed));
        changed[10] ^= 0xFF;
        Assert.False(CheatFingerprint.Matches(fp, changed));
        Assert.False(CheatFingerprint.Distinctive(new byte[8]));
        Assert.Equal(new[] { -16, -8, 2, 10 }, CheatFingerprint.Anchors(fp, 2).Select(a => a.Delta));
        Assert.Equal(fp, CheatFingerprint.FromHex(CheatFingerprint.ToHex(fp)));
    }

    [Fact]
    public void Session_RefindsAMovedValueByItsFingerprint()
    {
        var data = new byte[256];
        new Random(7).NextBytes(data);
        data[100] = 42;                                  // la valeur (octet), entourée d'octets variés
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        var file = Path.Combine(Path.GetTempPath(), $"oxyn-cheats-{Guid.NewGuid():N}.json");
        try
        {
            var addr = (long)pin.AddrOfPinnedObject() + 100;
            using var mem = new ProcessMemory(Environment.ProcessId);
            var fp = CheatFingerprint.Read(mem, addr, 1)!;
            var store = new CheatStore(file);
            // Position périmée (allocation inexistante) : seule l'empreinte permet de la retrouver.
            store.For("jeu").Add(new SavedCheat { Name = "Bombes", Locations = { new CheatLocation(12345, 0, CheatType.Byte, CheatFingerprint.ToHex(fp)) } });
            using var session = new CheatSession(Environment.ProcessId, "jeu", store);
            var cheat = Assert.Single(session.Cheats);
            Assert.False(cheat.Found);
            Assert.True(session.NeedsRelocation);
            session.Relocate(null, CancellationToken.None);
            Assert.Contains(cheat.Addresses, a => a.Addr == addr);
            Assert.Equal(42, session.Value(cheat));
            Assert.False(session.NeedsRelocation);
        }
        finally { pin.Free(); File.Delete(file); }
    }

    [Fact]
    public void Store_RoundTripsSavedValues()
    {
        var file = Path.Combine(Path.GetTempPath(), $"oxyn-cheats-{Guid.NewGuid():N}.json");
        try
        {
            var store = new CheatStore(file);
            store.For("zelda").Add(new SavedCheat { Name = "Bombes", Locations = { new CheatLocation(4L << 30, 0x1234, CheatType.Byte) } });
            store.For("vide");
            store.Save();
            var again = new CheatStore(file);
            var c = Assert.Single(again.For("zelda"));
            Assert.Equal("Bombes", c.Name);
            Assert.Equal(new CheatLocation(4L << 30, 0x1234, CheatType.Byte), c.Locations[0]);
            Assert.DoesNotContain("\"vide\"", File.ReadAllText(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Store_KeepsValuesAddedAfterTheGameWasEmptied()
    {
        // Bug du 14/11 : « Vies » supprimée (liste vide) puis « Bombes » ajoutée -> fichier enregistré vide.
        var file = Path.Combine(Path.GetTempPath(), $"oxyn-cheats-{Guid.NewGuid():N}.json");
        try
        {
            var store = new CheatStore(file);
            var list = store.For("zelda");
            list.Add(new SavedCheat { Name = "Vies" });
            store.Save();
            list.Clear();
            store.Save();
            list.Add(new SavedCheat { Name = "Bombes", Locations = { new CheatLocation(1, 2, CheatType.Int32) } });
            store.Save();
            Assert.Equal("Bombes", Assert.Single(new CheatStore(file).For("zelda")).Name);
        }
        finally { File.Delete(file); }
    }
}
