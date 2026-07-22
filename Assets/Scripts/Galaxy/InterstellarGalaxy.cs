using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct InterstellarCoordinate : IEquatable<InterstellarCoordinate>
{
    public long x;
    public long y;
    public long z;

    public static readonly InterstellarCoordinate Zero = new InterstellarCoordinate(0L, 0L, 0L);

    public InterstellarCoordinate(long x, long y, long z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public bool Equals(InterstellarCoordinate other) => x == other.x && y == other.y && z == other.z;
    public override bool Equals(object value) => value is InterstellarCoordinate other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = x.GetHashCode();
            hash = hash * 397 ^ y.GetHashCode();
            return hash * 397 ^ z.GetHashCode();
        }
    }

    public override string ToString() => $"{x}:{y}:{z}";
    public static bool operator ==(InterstellarCoordinate left, InterstellarCoordinate right) => left.Equals(right);
    public static bool operator !=(InterstellarCoordinate left, InterstellarCoordinate right) => !left.Equals(right);
}

[Serializable]
public struct DoubleVector3
{
    public double x;
    public double y;
    public double z;

    public static readonly DoubleVector3 Zero = new DoubleVector3(0d, 0d, 0d);

    public DoubleVector3(double x, double y, double z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public Vector3 ToVector3() => new Vector3((float)x, (float)y, (float)z);

    public static DoubleVector3 operator +(DoubleVector3 left, DoubleVector3 right)
        => new DoubleVector3(left.x + right.x, left.y + right.y, left.z + right.z);

    public static DoubleVector3 operator -(DoubleVector3 left, DoubleVector3 right)
        => new DoubleVector3(left.x - right.x, left.y - right.y, left.z - right.z);
}

public sealed class ProceduralInterstellarGenerator
{
    public const int CurrentVersion = 1;
    public const double SectorSpacing = 12000d;
    const long MacroCellSize = 4L;

    readonly int worldSeed;
    readonly ProceduralGalaxyGenerator planetContentGenerator;

    public ProceduralInterstellarGenerator(int worldSeed, IReadOnlyList<GalaxyResourceCatalogEntry> resources)
    {
        this.worldSeed = worldSeed;
        planetContentGenerator = new ProceduralGalaxyGenerator(worldSeed, resources);
    }

    public bool HasPlanet(InterstellarCoordinate coordinate)
    {
        if (coordinate == InterstellarCoordinate.Zero)
            return true;

        long macroX = FloorDivide(coordinate.x, MacroCellSize);
        long macroY = FloorDivide(coordinate.y, MacroCellSize);
        long macroZ = FloorDivide(coordinate.z, MacroCellSize);
        InterstellarCoordinate candidate = GetMacroCellCandidate(macroX, macroY, macroZ);
        if (candidate != coordinate)
            return false;

        return AbsDistance(coordinate.x) >= 3L
            || AbsDistance(coordinate.y) >= 3L
            || AbsDistance(coordinate.z) >= 3L;
    }

    public GalaxyPlanetDefinition GeneratePlanet(InterstellarCoordinate coordinate)
    {
        if (!HasPlanet(coordinate))
            return null;

        ulong hash = Hash(worldSeed, coordinate, 0xA0761D6478BD642FUL);
        GalaxyPlanetDefinition definition = planetContentGenerator.GeneratePlanetFromStableHash(
            hash,
            EncodePlanetId(coordinate),
            coordinate == InterstellarCoordinate.Zero);
        definition.coordinate3D = coordinate;
        definition.isInterstellar = true;
        return definition;
    }

    public DoubleVector3 GetPlanetUniversePosition(InterstellarCoordinate coordinate)
    {
        ulong hash = Hash(worldSeed, coordinate, 0xE7037ED1A0B428DBUL);
        double jitterRange = SectorSpacing * 0.22d;
        double jitterX = SignedUnit(hash) * jitterRange;
        double jitterY = SignedUnit(Mix(hash ^ 0x8EBC6AF09C88C6E3UL)) * SectorSpacing * 0.38d;
        double jitterZ = SignedUnit(Mix(hash ^ 0x589965CC75374CC3UL)) * jitterRange;
        if (coordinate == InterstellarCoordinate.Zero)
            return DoubleVector3.Zero;
        return new DoubleVector3(
            coordinate.x * SectorSpacing + jitterX,
            coordinate.y * SectorSpacing + jitterY,
            coordinate.z * SectorSpacing + jitterZ);
    }

    public static string EncodePlanetId(InterstellarCoordinate coordinate)
        => $"q_{ToBase36(ZigZag(coordinate.x))}_{ToBase36(ZigZag(coordinate.y))}_{ToBase36(ZigZag(coordinate.z))}";

    public static ulong Hash(int seed, InterstellarCoordinate coordinate, ulong salt)
    {
        ulong value = unchecked((uint)seed) ^ salt;
        value = Mix(value ^ unchecked((ulong)coordinate.x));
        value = Mix(value ^ RotateLeft(unchecked((ulong)coordinate.y), 21));
        return Mix(value ^ RotateLeft(unchecked((ulong)coordinate.z), 42));
    }

    InterstellarCoordinate GetMacroCellCandidate(long macroX, long macroY, long macroZ)
    {
        var macro = new InterstellarCoordinate(macroX, macroY, macroZ);
        ulong hash = Hash(worldSeed, macro, 0xD6E8FEB86659FD93UL);
        long offsetX = 1L + (long)(hash % 2UL);
        long offsetY = 1L + (long)((hash >> 8) % 2UL);
        long offsetZ = 1L + (long)((hash >> 16) % 2UL);
        return new InterstellarCoordinate(
            macroX * MacroCellSize + offsetX,
            macroY * MacroCellSize + offsetY,
            macroZ * MacroCellSize + offsetZ);
    }

    static double SignedUnit(ulong value) => ((value >> 11) * (1d / 9007199254740992d)) * 2d - 1d;
    static ulong ZigZag(long value) => unchecked((ulong)((value << 1) ^ (value >> 63)));
    static long AbsDistance(long value) => value == long.MinValue ? long.MaxValue : Math.Abs(value);

    static long FloorDivide(long value, long divisor)
    {
        long quotient = value / divisor;
        long remainder = value % divisor;
        return remainder < 0L ? quotient - 1L : quotient;
    }

    static string ToBase36(ulong value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value == 0UL)
            return "0";
        char[] buffer = new char[13];
        int index = buffer.Length;
        while (value > 0UL)
        {
            buffer[--index] = digits[(int)(value % 36UL)];
            value /= 36UL;
        }
        return new string(buffer, index, buffer.Length - index);
    }

    static ulong RotateLeft(ulong value, int shift) => (value << shift) | (value >> (64 - shift));

    static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
