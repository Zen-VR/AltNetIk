using LiteNetLib.Utils;
using System.Numerics;
using System;

namespace AltNetIk
{
    internal static class Serializers
    {
        public static void SerializeQuaternion(NetDataWriter writer, Quaternion quaternion)
        {
            writer.Put(quaternion.X);
            writer.Put(quaternion.Y);
            writer.Put(quaternion.Z);
            writer.Put(quaternion.W);
        }

        public static Quaternion DeserializeQuaternion(NetDataReader reader)
        {
            float x = reader.GetFloat();
            float y = reader.GetFloat();
            float z = reader.GetFloat();
            float w = reader.GetFloat();

            return new Quaternion(x, y, z, w);
        }

        public static void SerializeQuaternionArray(NetDataWriter writer, Quaternion[] quaternions)
        {
            float[] floats = new float[quaternions.Length * 4];
            for (int i = 0; i < quaternions.Length; i += 4)
            {
                floats[i + 0] = quaternions[i].X;
                floats[i + 1] = quaternions[i].Y;
                floats[i + 2] = quaternions[i].Z;
                floats[i + 3] = quaternions[i].W;
            }
            writer.PutArray(floats);
        }

        public static Quaternion[] DeserializeQuaternionArray(NetDataReader reader)
        {
            float[] array = reader.GetFloatArray();

            int length = array.Length / 4;

            if (length * 4 != array.Length)
                throw new System.Exception("Invalid quaternion array length");

            Quaternion[] quaternions = new Quaternion[length];
            for (int i = 0; i < length; i += 4)
            {
                quaternions[i] = new Quaternion(array[i + 0], array[i + 1], array[i + 2], array[i + 3]);
            }

            return quaternions;
        }

        public static void SerializeVector3(NetDataWriter writer, Vector3 vector)
        {
            writer.Put(vector.X);
            writer.Put(vector.Y);
            writer.Put(vector.Z);
        }

        public static Vector3 DeserializeVector3(NetDataReader reader)
        {
            float x = reader.GetFloat();
            float y = reader.GetFloat();
            float z = reader.GetFloat();

            return new Vector3(x, y, z);
        }

        public static void SerializeVector3Array(NetDataWriter writer, Vector3[] vectors)
        {
            float[] floats = new float[vectors.Length * 3];
            for (int i = 0; i < vectors.Length; i += 3)
            {
                floats[i + 0] = vectors[i].X;
                floats[i + 1] = vectors[i].Y;
                floats[i + 2] = vectors[i].Z;
            }
            writer.PutArray(floats);
        }

        public static Vector3[] DeserializeVector3Array(NetDataReader reader)
        {
            float[] array = reader.GetFloatArray();

            int length = array.Length / 3;

            if (length * 3 != array.Length)
                throw new System.Exception("Invalid vector array length");

            Vector3[] vectors = new Vector3[length];
            for (int i = 0; i < length; i += 3)
            {
                vectors[i] = new Vector3(array[i + 0], array[i + 1], array[i + 2]);
            }

            return vectors;
        }

        public static void SerializePackedBoolArray(NetDataWriter writer, bool[] bools)
        {
            // 6 first bits are used for length, the 58 remaining bits are used for the actual data (It just so happens that the max value of the unsigned 6-bit length is the same as the max amount of bools we can pack)
            if (bools.Length > 58)
                throw new System.Exception("Bool array exceeds maximum length of 58");

            ulong packed = 0;
            packed |= (ulong)bools.Length << 58;
            for (int i = 0; i < bools.Length; i++)
            {
                if (bools[i])
                    packed |= (ulong)1 << (57 - i);
            }

            writer.Put(packed);
        }

        public static bool[] DeserializePackedBoolArray(NetDataReader reader)
        {
            ulong packed = reader.GetULong();

            int length = (int)(packed >> 58);
            bool[] bools = new bool[length];
            for (int i = 0; i < length; i++)
            {
                bools[i] = (packed & (ulong)1 << (57 - i)) != 0;
            }

            return bools;
        }

        private static float ClampFloat(float val)
        {
            if (val < -1.0f)
            {
                val = -1.0f;
            }
            if (val > 1.0f)
            {
                val = 1.0f;
            }
            return val;
        }

        /// <summary>
        /// Serializes a float how VRChat would normally for their parameters
        /// <br />
        /// Reference: https://github.com/lyuma/Av3Emulator/blob/master/Scripts/LyumaAv3Runtime.cs#L320
        /// </summary>
        /// <param name="value">Value to Serialize</param>
        /// <returns>Serialize byte representing a float</returns>
        public static byte SerializeFloat(float value)
        {
            value = ClampFloat(value);
            value *= 127.0f;
            value = (float)Math.Round(value);
            return (byte)(sbyte)value;
        }

        public static float DeserializeFloat(byte rawByte)
        {
            float value = ((sbyte)rawByte) / 127.0f;
            return ClampFloat(value);
        }
    }
}