using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace Avt.Bereinigen;

/// <summary>
/// Fängt beim DwgOut eines Objekts alle Verweise auf andere Objekte (Zeiger und Besitz) und die Texte. So sieht man auch
/// Verweise von Objekten ohne .NET-Schnittstelle (z. B. Anzeigethemen). Nur lesen. Grenzen: reine Handles (z. B. XData
/// 1005) zählen nicht; persistente Reaktoren zählen als Zeiger (schützt eher zu viel).
/// </summary>
internal sealed class Zeigerfang : DwgFiler
{
    public List<(bool Besitz, ObjectId Ziel)> Verweise { get; } = [];
    public List<string> Texte { get; } = [];

    private void Merke(bool besitz, ObjectId id)
    {
        if (!id.IsNull) Verweise.Add((besitz, id));
    }

    public override ErrorStatus FilerStatus { get => ErrorStatus.OK; set { } }
    public override FilerType FilerType => FilerType.CopyFiler;
    // Position bleibt 0 (Falle aus ASG: mit mitgezählter Position liefern Anzeigethemen keine Regeln).
    public override long Position => 0;
    public override void ResetFilerStatus() { }
    public override void Seek(long offset, int method) { }

    public override void WriteHardOwnershipId(ObjectId value) => Merke(true, value);
    public override void WriteSoftOwnershipId(ObjectId value) => Merke(true, value);
    public override void WriteHardPointerId(ObjectId value) => Merke(false, value);
    public override void WriteSoftPointerId(ObjectId value) => Merke(false, value);
    public override void WriteString(string value) { if (!string.IsNullOrEmpty(value)) Texte.Add(value); }

    public override void WriteAddress(IntPtr value) { }
    public override void WriteBinaryChunk(byte[] chunk) { }
    public override void WriteBoolean(bool value) { }
    public override void WriteByte(byte value) { }
    public override void WriteBytes(byte[] value) { }
    public override void WriteDouble(double value) { }
    public override void WriteHandle(Handle handle) { }
    public override void WriteInt16(short value) { }
    public override void WriteInt32(int value) { }
    public override void WriteInt64(long value) { }
    public override void WritePoint2d(Point2d value) { }
    public override void WritePoint3d(Point3d value) { }
    public override void WriteScale3d(Scale3d value) { }
    public override void WriteUInt16(ushort value) { }
    public override void WriteUInt32(uint value) { }
    public override void WriteUInt64(ulong value) { }
    public override void WriteVector2d(Vector2d value) { }
    public override void WriteVector3d(Vector3d value) { }

    public override IntPtr ReadAddress() => throw new NotSupportedException();
    public override byte[] ReadBinaryChunk() => throw new NotSupportedException();
    public override bool ReadBoolean() => throw new NotSupportedException();
    public override byte ReadByte() => throw new NotSupportedException();
    public override void ReadBytes(byte[] value) => throw new NotSupportedException();
    public override double ReadDouble() => throw new NotSupportedException();
    public override Handle ReadHandle() => throw new NotSupportedException();
    public override ObjectId ReadHardOwnershipId() => throw new NotSupportedException();
    public override ObjectId ReadHardPointerId() => throw new NotSupportedException();
    public override short ReadInt16() => throw new NotSupportedException();
    public override int ReadInt32() => throw new NotSupportedException();
    public override long ReadInt64() => throw new NotSupportedException();
    public override Point2d ReadPoint2d() => throw new NotSupportedException();
    public override Point3d ReadPoint3d() => throw new NotSupportedException();
    public override Scale3d ReadScale3d() => throw new NotSupportedException();
    public override ObjectId ReadSoftOwnershipId() => throw new NotSupportedException();
    public override ObjectId ReadSoftPointerId() => throw new NotSupportedException();
    public override string ReadString() => throw new NotSupportedException();
    public override ushort ReadUInt16() => throw new NotSupportedException();
    public override uint ReadUInt32() => throw new NotSupportedException();
    public override ulong ReadUInt64() => throw new NotSupportedException();
    public override Vector2d ReadVector2d() => throw new NotSupportedException();
    public override Vector3d ReadVector3d() => throw new NotSupportedException();
}
