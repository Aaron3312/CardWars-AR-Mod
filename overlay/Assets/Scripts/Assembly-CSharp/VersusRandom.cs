using UnityEngine;

// Battle randomness. Outside a versus match it is plain UnityEngine.Random, as before. In a versus match
// both phones draw from the same seeded generators, one stream per canonical side (A = host, B = guest),
// picked by the owner of the effect. So the order is the same on both phones even though each one sees
// itself as PlayerType.User. Cosmetic randomness (particles, sounds, the battle ring) stays on Unity's.
public static class VersusRandom
{
	private static readonly ulong[] state0 = new ulong[2];

	private static readonly ulong[] state1 = new ulong[2];

	private static readonly int[] draws = new int[2];

	private static bool seeded;

	public static bool Seeded
	{
		get
		{
			return seeded;
		}
	}

	public static void Seed(int seed)
	{
		for (int side = 0; side < 2; side++)
		{
			ulong x = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)(side + 1) * 0xD1B54A32D192ED03UL;
			state0[side] = SplitMix(ref x);
			state1[side] = SplitMix(ref x);
			draws[side] = 0;
		}
		seeded = true;
	}

	public static void Clear()
	{
		seeded = false;
	}

	// Draws taken from each canonical stream so far (part of the per-turn state hash).
	public static int Draws(int side)
	{
		return draws[side];
	}

	// Like UnityEngine.Random.Range(int, int): min inclusive, max exclusive.
	public static int Range(PlayerType owner, int min, int max)
	{
		if (!seeded)
		{
			return Random.Range(min, max);
		}
		if (max <= min)
		{
			return min;
		}
		return min + (int)(Next(VersusMatch.Side(owner)) % (ulong)(max - min));
	}

	// Like UnityEngine.Random.Range(float, float).
	public static float Range(PlayerType owner, float min, float max)
	{
		if (!seeded)
		{
			return Random.Range(min, max);
		}
		return min + (max - min) * Value(owner);
	}

	// Uniform in [0, 1).
	public static float Value(PlayerType owner)
	{
		if (!seeded)
		{
			return Random.value;
		}
		return (Next(VersusMatch.Side(owner)) >> 40) * (1f / 16777216f);
	}

	private static ulong Next(int side)
	{
		// xorshift128+
		ulong s1 = state0[side];
		ulong s0 = state1[side];
		state0[side] = s0;
		s1 ^= s1 << 23;
		state1[side] = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
		draws[side]++;
		return state1[side] + s0;
	}

	private static ulong SplitMix(ref ulong x)
	{
		x += 0x9E3779B97F4A7C15UL;
		ulong z = x;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return z ^ (z >> 31);
	}
}
