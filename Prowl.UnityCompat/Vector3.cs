// Unity-style Vector3 backed by Prowl.Vector.Float3.

using System;
using Prowl.Vector;

namespace UnityEngine;

/// <summary>
/// Thin Unity-compatible wrapper around <see cref="Float3"/>. Implicit conversions in both directions let
/// Prowl APIs accept it directly, so no Transform/GameObject aliases are needed.
/// </summary>
public struct Vector3 : IEquatable<Vector3>
{
    private Float3 _value;

    public Vector3(float x, float y, float z) => _value = new Float3(x, y, z);
    public Vector3(float scalar) => _value = new Float3(scalar);
    private Vector3(Float3 value) => _value = value;

    public float x { get => _value.X; set => _value.X = value; }
    public float y { get => _value.Y; set => _value.Y = value; }
    public float z { get => _value.Z; set => _value.Z = value; }

    public float this[int index]
    {
        get => _value[index];
        set => _value[index] = value;
    }

    public static implicit operator Vector3(Float3 value) => new Vector3(value);
    public static implicit operator Float3(Vector3 value) => value._value;

    public float magnitude => Float3.Length(_value);
    public float sqrMagnitude => Float3.LengthSquared(_value);
    public Vector3 normalized => new Vector3(Float3.Normalize(_value));

    public void Normalize() => _value = Float3.Normalize(_value);

    public static Vector3 zero => new Vector3(0f, 0f, 0f);
    public static Vector3 one => new Vector3(1f, 1f, 1f);
    public static Vector3 up => new Vector3(0f, 1f, 0f);
    public static Vector3 down => new Vector3(0f, -1f, 0f);
    public static Vector3 right => new Vector3(1f, 0f, 0f);
    public static Vector3 left => new Vector3(-1f, 0f, 0f);
    public static Vector3 forward => new Vector3(0f, 0f, 1f);
    public static Vector3 back => new Vector3(0f, 0f, -1f);

    public static float Dot(Vector3 a, Vector3 b) => Float3.Dot(a._value, b._value);
    public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(Float3.Cross(a._value, b._value));
    public static float Distance(Vector3 a, Vector3 b) => Float3.Distance(a._value, b._value);

    public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a._value + b._value);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a._value - b._value);
    public static Vector3 operator -(Vector3 v) => new Vector3(-v._value);
    public static Vector3 operator *(Vector3 v, float scalar) => new Vector3(v._value * scalar);
    public static Vector3 operator *(float scalar, Vector3 v) => new Vector3(scalar * v._value);
    public static Vector3 operator /(Vector3 v, float scalar) => new Vector3(v._value / scalar);

    public static bool operator ==(Vector3 a, Vector3 b) => a.Equals(b);
    public static bool operator !=(Vector3 a, Vector3 b) => !a.Equals(b);

    public bool Equals(Vector3 other) => _value.Equals(other._value);
    public override bool Equals(object? obj) => obj is Vector3 other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();

    public override string ToString() => $"({x:F3}, {y:F3}, {z:F3})";
}
