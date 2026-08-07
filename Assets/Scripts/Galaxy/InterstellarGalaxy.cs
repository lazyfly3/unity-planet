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
    public const int CurrentVersion = 3;
    public const long MacroCellSize = 4L;
    public const int StarterSystemPlanetCount = 6;
    public const double SystemSpacingMeters = PhysicalConstants.LightYear * 6d;
    // Compatibility address spacing. Physical positions come from the system
    // barycenter plus a Kepler orbit and must not be reconstructed from this value.
    public const double SectorSpacing = SystemSpacingMeters / MacroCellSize;

    readonly int worldSeed;
    readonly ProceduralGalaxyGenerator planetContentGenerator;

    public ProceduralInterstellarGenerator(int worldSeed, IReadOnlyList<GalaxyResourceCatalogEntry> resources)
    {
        this.worldSeed = worldSeed;
        planetContentGenerator = new ProceduralGalaxyGenerator(worldSeed, resources);
    }

    public bool HasPlanet(InterstellarCoordinate coordinate)
    {
        long macroX = FloorDivide(coordinate.x, MacroCellSize);
        long macroY = FloorDivide(coordinate.y, MacroCellSize);
        long macroZ = FloorDivide(coordinate.z, MacroCellSize);
        return GetOrbitIndex(
            coordinate,
            new InterstellarCoordinate(macroX, macroY, macroZ)) >= 0;
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
        InterstellarCoordinate systemCoordinate = GetSystemCoordinate(coordinate);
        definition.systemId = EncodeSystemId(systemCoordinate);
        definition.orbitIndex = GetOrbitIndex(coordinate, systemCoordinate);
        definition.orbit = CreateOrbit(
            coordinate,
            systemCoordinate,
            definition.orbitIndex,
            GetPlanetCount(systemCoordinate));
        return definition;
    }

    public DoubleVector3 GetPlanetUniversePosition(InterstellarCoordinate coordinate)
    {
        return GetPlanetUniversePosition(coordinate, 0d);
    }

    public DoubleVector3 GetPlanetUniversePosition(
        InterstellarCoordinate coordinate,
        double universeTimeSeconds)
        => GetPlanetUniverseAddress(coordinate, universeTimeSeconds).ToAbsoluteMeters();

    public UniversePosition GetPlanetUniverseAddress(
        InterstellarCoordinate coordinate,
        double universeTimeSeconds)
    {
        InterstellarCoordinate systemCoordinate = GetSystemCoordinate(coordinate);
        StellarSystemDefinition system = GenerateSystem(systemCoordinate);
        CelestialBodyState state = GetPlanetBarycentricState(
            coordinate,
            universeTimeSeconds);
        return new UniversePosition(
            systemCoordinate,
            system.barycenterOffsetMeters + state.positionMeters);
    }

    public CelestialBodyState GetPlanetBarycentricState(
        InterstellarCoordinate coordinate,
        double universeTimeSeconds)
    {
        InterstellarCoordinate systemCoordinate = GetSystemCoordinate(coordinate);
        StellarSystemDefinition system = GenerateSystem(systemCoordinate);
        int orbitIndex = Mathf.Max(0, GetOrbitIndex(coordinate, systemCoordinate));
        CelestialOrbitDefinition orbit = CreateOrbit(
            coordinate,
            systemCoordinate,
            orbitIndex,
            GetPlanetCount(systemCoordinate));
        return CelestialEphemeris.GetBodyState(
            orbit,
            system.stellarMassKg,
            universeTimeSeconds);
    }

    public StellarSystemDefinition GenerateSystem(InterstellarCoordinate systemCoordinate)
    {
        ulong hash = Hash(worldSeed, systemCoordinate, 0xE7037ED1A0B428DBUL);
        double stellarMassSolar = Lerp(
            0.3d,
            1.4d,
            Unit(Mix(hash ^ 0x8EBC6AF09C88C6E3UL)));
        double jitterRange = PhysicalConstants.LightYear * 1.15d;
        return new StellarSystemDefinition
        {
            systemId = EncodeSystemId(systemCoordinate),
            systemCoordinate = systemCoordinate,
            stellarMassKg = stellarMassSolar * PhysicalConstants.SolarMass,
            stellarRadiusMeters = PhysicalConstants.SolarRadius
                * Math.Pow(stellarMassSolar, 0.8d),
            luminositySolar = Math.Pow(stellarMassSolar, 3.5d),
            barycenterOffsetMeters = new DoubleVector3(
                SignedUnit(hash) * jitterRange,
                SignedUnit(Mix(hash ^ 0x589965CC75374CC3UL)) * jitterRange * 0.55d,
                SignedUnit(Mix(hash ^ 0xD6E8FEB86659FD93UL)) * jitterRange)
        };
    }

    public InterstellarCoordinate GetSystemCoordinate(InterstellarCoordinate planetCoordinate)
        => new InterstellarCoordinate(
            FloorDivide(planetCoordinate.x, MacroCellSize),
            FloorDivide(planetCoordinate.y, MacroCellSize),
            FloorDivide(planetCoordinate.z, MacroCellSize));

    public InterstellarCoordinate GetRepresentativePlanetCoordinate(
        InterstellarCoordinate systemCoordinate)
        => GetPrimaryCandidate(
            systemCoordinate.x,
            systemCoordinate.y,
            systemCoordinate.z);

    public InterstellarCoordinate GetSystemCoordinateFromAbsolutePosition(DoubleVector3 position)
        => new InterstellarCoordinate(
            RoundToLong(position.x / SystemSpacingMeters),
            RoundToLong(position.y / SystemSpacingMeters),
            RoundToLong(position.z / SystemSpacingMeters));

    int GetPlanetCount(InterstellarCoordinate systemCoordinate)
    {
        // Every new save starts in the origin system. Keep that first chapter
        // deterministic and large enough to provide a six-step difficulty
        // progression, regardless of the player's world seed.
        if (systemCoordinate == InterstellarCoordinate.Zero)
            return StarterSystemPlanetCount;

        ulong hash = Hash(worldSeed, systemCoordinate, 0xA24BAED4963EE407UL);
        return 2 + (int)(hash % 5UL);
    }

    int GetOrbitIndex(
        InterstellarCoordinate coordinate,
        InterstellarCoordinate systemCoordinate)
    {
        InterstellarCoordinate primary = GetPrimaryCandidate(
            systemCoordinate.x,
            systemCoordinate.y,
            systemCoordinate.z);
        if (coordinate == primary)
            return 0;

        if (GetSystemCoordinate(coordinate) != systemCoordinate)
            return -1;

        int additionalCount = GetPlanetCount(systemCoordinate) - 1;
        ulong rank = Hash(worldSeed, coordinate, 0x9FB21C651E98DF25UL);
        int lowerRanks = 0;
        for (long z = systemCoordinate.z * MacroCellSize;
             z < (systemCoordinate.z + 1L) * MacroCellSize;
             z++)
        for (long y = systemCoordinate.y * MacroCellSize;
             y < (systemCoordinate.y + 1L) * MacroCellSize;
             y++)
        for (long x = systemCoordinate.x * MacroCellSize;
             x < (systemCoordinate.x + 1L) * MacroCellSize;
             x++)
        {
            var other = new InterstellarCoordinate(x, y, z);
            if (other == primary || other == coordinate)
                continue;
            ulong otherRank = Hash(worldSeed, other, 0x9FB21C651E98DF25UL);
            if (otherRank < rank || (otherRank == rank && Compare(other, coordinate) < 0))
                lowerRanks++;
        }
        return lowerRanks < additionalCount ? lowerRanks + 1 : -1;
    }

    CelestialOrbitDefinition CreateOrbit(
        InterstellarCoordinate coordinate,
        InterstellarCoordinate systemCoordinate,
        int orbitIndex,
        int planetCount)
    {
        ulong hash = Hash(worldSeed, coordinate, 0xC13FA9A902A6328FUL);
        double normalized = planetCount <= 1
            ? 0.5d
            : orbitIndex / (double)(planetCount - 1);
        bool originSystem = systemCoordinate == InterstellarCoordinate.Zero;
        double semiMajorAxisAu = originSystem
            ? Math.Pow(30d, normalized)
            : 0.1d * Math.Pow(300d, normalized)
                * Lerp(0.88d, 1.12d, Unit(hash));
        return new CelestialOrbitDefinition
        {
            semiMajorAxisMeters = semiMajorAxisAu * PhysicalConstants.AstronomicalUnit,
            eccentricity = (float)Lerp(0d, 0.12d, Unit(Mix(hash ^ 0x8CB92BA72F3D8DD7UL))),
            inclinationDegrees = (float)Lerp(-8d, 8d, Unit(Mix(hash ^ 0xDB4F0B9175AE2165UL))),
            longitudeAscendingNodeDegrees = (float)(Unit(Mix(hash ^ 0xBBE0563303A4615FUL)) * 360d),
            argumentOfPeriapsisDegrees = (float)(Unit(Mix(hash ^ 0xA0F2EC75A1FE1575UL)) * 360d),
            meanAnomalyAtEpochDegrees = (float)(Unit(Mix(hash ^ 0x89E182857D9ED689UL)) * 360d),
            epochSeconds = 0d
        };
    }

    public static string EncodePlanetId(InterstellarCoordinate coordinate)
        => $"q_{ToBase36(ZigZag(coordinate.x))}_{ToBase36(ZigZag(coordinate.y))}_{ToBase36(ZigZag(coordinate.z))}";

    public static string EncodeSystemId(InterstellarCoordinate coordinate)
        => $"sys_{ToBase36(ZigZag(coordinate.x))}_{ToBase36(ZigZag(coordinate.y))}_{ToBase36(ZigZag(coordinate.z))}";

    public static bool TryDecodePlanetId(
        string planetId,
        out InterstellarCoordinate coordinate)
    {
        coordinate = default;
        if (string.IsNullOrEmpty(planetId))
            return false;
        string[] components = planetId.Split('_');
        if (components.Length != 4
            || !string.Equals(components[0], "q", StringComparison.Ordinal)
            || !TryFromBase36(components[1], out ulong x)
            || !TryFromBase36(components[2], out ulong y)
            || !TryFromBase36(components[3], out ulong z))
        {
            return false;
        }
        coordinate = new InterstellarCoordinate(
            UnZigZag(x),
            UnZigZag(y),
            UnZigZag(z));
        return true;
    }

    public static ulong Hash(int seed, InterstellarCoordinate coordinate, ulong salt)
    {
        ulong value = unchecked((uint)seed) ^ salt;
        value = Mix(value ^ unchecked((ulong)coordinate.x));
        value = Mix(value ^ RotateLeft(unchecked((ulong)coordinate.y), 21));
        return Mix(value ^ RotateLeft(unchecked((ulong)coordinate.z), 42));
    }

    InterstellarCoordinate GetPrimaryCandidate(long macroX, long macroY, long macroZ)
    {
        if (macroX == 0L && macroY == 0L && macroZ == 0L)
            return InterstellarCoordinate.Zero;
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
    static double Unit(ulong value) => (value >> 11) * (1d / 9007199254740992d);
    static double Lerp(double left, double right, double value) => left + (right - left) * value;
    static ulong ZigZag(long value) => unchecked((ulong)((value << 1) ^ (value >> 63)));
    static long UnZigZag(ulong value)
        => unchecked((long)(value >> 1)) ^ -unchecked((long)(value & 1UL));

    static long FloorDivide(long value, long divisor)
    {
        long quotient = value / divisor;
        long remainder = value % divisor;
        return remainder < 0L ? quotient - 1L : quotient;
    }

    static long RoundToLong(double value)
    {
        if (value >= long.MaxValue)
            return long.MaxValue;
        if (value <= long.MinValue)
            return long.MinValue;
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    static int Compare(InterstellarCoordinate left, InterstellarCoordinate right)
    {
        int x = left.x.CompareTo(right.x);
        if (x != 0)
            return x;
        int y = left.y.CompareTo(right.y);
        return y != 0 ? y : left.z.CompareTo(right.z);
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

    static bool TryFromBase36(string value, out ulong result)
    {
        result = 0UL;
        if (string.IsNullOrEmpty(value))
            return false;
        for (int index = 0; index < value.Length; index++)
        {
            char character = char.ToLowerInvariant(value[index]);
            int digit = character >= '0' && character <= '9'
                ? character - '0'
                : character >= 'a' && character <= 'z'
                    ? character - 'a' + 10
                    : -1;
            if (digit < 0 || digit >= 36
                || result > (ulong.MaxValue - (ulong)digit) / 36UL)
            {
                result = 0UL;
                return false;
            }
            result = result * 36UL + (ulong)digit;
        }
        return true;
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
