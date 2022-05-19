using LiteNetLib.Utils;

namespace AltNetIk
{
    internal static class Serializers
    {
        public static void SerializeQuaternion(NetDataWriter writer, UnityEngine.Quaternion quaternion)
        {
            writer.Put(quaternion.x);
            writer.Put(quaternion.y);
            writer.Put(quaternion.z);
            writer.Put(quaternion.w);
        }
        public static UnityEngine.Quaternion DeserializeQuaternion(NetDataReader reader)
        {
            float x = reader.GetFloat();
            float y = reader.GetFloat();
            float z = reader.GetFloat();
            float w = reader.GetFloat();

            return new UnityEngine.Quaternion(x, y, z, w);
        }
        public static void SerializeQuaternionArray(NetDataWriter writer, UnityEngine.Quaternion[] quaternions)
        {
            float[] floats = new float[quaternions.Length * 4];
            for (int i = 0; i < quaternions.Length; i += 4)
            {
                floats[i + 0] = quaternions[i].x;
                floats[i + 1] = quaternions[i].y;
                floats[i + 2] = quaternions[i].z;
                floats[i + 3] = quaternions[i].w;
            }
            writer.PutArray(floats);
        }
        public static UnityEngine.Quaternion[] DeserializeQuaternionArray(NetDataReader reader)
        {
            float[] array = reader.GetFloatArray();

            int length = array.Length / 4;

            if (length * 4 != array.Length)
                throw new System.Exception("Invalid quaternion array length");

            UnityEngine.Quaternion[] quaternions = new UnityEngine.Quaternion[length];
            for (int i = 0; i < length; i += 4)
            {
                quaternions[i] = new UnityEngine.Quaternion(array[i + 0], array[i + 1], array[i + 2], array[i + 3]);
            }
            
            return quaternions;
        }

        public static void SerializeVector3(NetDataWriter writer, UnityEngine.Vector3 vector)
        {
            writer.Put(vector.x);
            writer.Put(vector.y);
            writer.Put(vector.z);
        }
        public static UnityEngine.Vector3 DeserializeVector3(NetDataReader reader)
        {
            float x = reader.GetFloat();
            float y = reader.GetFloat();
            float z = reader.GetFloat();

            return new UnityEngine.Vector3(x, y, z);
        }
        public static void SerializeVector3Array(NetDataWriter writer, UnityEngine.Vector3[] vectors)
        {
            float[] floats = new float[vectors.Length * 3];
            for (int i = 0; i < vectors.Length; i += 3)
            {
                floats[i + 0] = vectors[i].x;
                floats[i + 1] = vectors[i].y;
                floats[i + 2] = vectors[i].z;
            }
            writer.PutArray(floats);
        }
        public static UnityEngine.Vector3[] DeserializeVector3Array(NetDataReader reader)
        {
            float[] array = reader.GetFloatArray();

            int length = array.Length / 3;

            if (length * 3 != array.Length)
                throw new System.Exception("Invalid vector array length");

            UnityEngine.Vector3[] vectors = new UnityEngine.Vector3[length];
            for (int i = 0; i < length; i += 3)
            {
                vectors[i] = new UnityEngine.Vector3(array[i + 0], array[i + 1], array[i + 2]);
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
    }
}
