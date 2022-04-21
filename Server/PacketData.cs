using LiteNetLib.Utils;

public class PacketData : INetSerializable
{
    public struct Vector3 : INetSerializable
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float X, float Y, float Z)
        {
            x = X;
            y = Y;
            z = Z;
        }
        public void Serialize(NetDataWriter writer)
        {
            writer.Put(x);
            writer.Put(y);
            writer.Put(z);
        }
        public void Deserialize(NetDataReader reader)
        {
            x = reader.GetFloat();
            y = reader.GetFloat();
            z = reader.GetFloat();
        }
    }
    public struct Quaternion : INetSerializable
    {
        public float x;
        public float y;
        public float z;
        public float w;
        public Quaternion(float X, float Y, float Z, float W)
        {
            x = X;
            y = Y;
            z = Z;
            w = W;
        }
        public void Serialize(NetDataWriter writer)
        {
            writer.Put(x);
            writer.Put(y);
            writer.Put(z);
            writer.Put(w);
        }
        public void Deserialize(NetDataReader reader)
        {
            x = reader.GetFloat();
            y = reader.GetFloat();
            z = reader.GetFloat();
            w = reader.GetFloat();
        }
    }

    public int photonId { get; set; }
    public bool loading { get; set; }
    public bool frozen { get; set; }
    public byte boneCount { get; set; }
    public Vector3 hipPosition { get; set; }
    public Vector3 playerPosition { get; set; }
    public Quaternion playerRotation { get; set; }
    public bool[] boneList { get; set; }
    public Quaternion[] boneRotations { get; set; }


    public void Serialize(NetDataWriter writer)
    {
        writer.Put(photonId);
        writer.Put(loading);
        writer.Put(frozen);
        writer.Put(boneCount);

        writer.Put(hipPosition);
        writer.Put(playerPosition);
        writer.Put(playerRotation);

        writer.PutArray(boneList);

        foreach (Quaternion boneRotation in boneRotations)
        {
            writer.Put(boneRotation);
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        photonId = reader.GetInt();
        loading = reader.GetBool();
        frozen = reader.GetBool();
        boneCount = reader.GetByte();

        hipPosition = reader.Get<Vector3>();
        playerPosition = reader.Get<Vector3>();
        playerRotation = reader.Get<Quaternion>();

        boneList = reader.GetBoolArray();

        boneRotations = new Quaternion[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            boneRotations[i] = reader.Get<Quaternion>();
        }
    }
}

public class ParamData : INetSerializable
{
    public int photonId { get; set; }
    public string[] paramName { get; set; }
    public short[] paramType { get; set; }
    public float[] paramValue { get; set; }
    public void Serialize(NetDataWriter writer)
    {
        writer.Put(photonId);

        writer.PutArray(paramName);
        writer.PutArray(paramType);
        writer.PutArray(paramValue);
    }
    public void Deserialize(NetDataReader reader)
    {
        photonId = reader.GetInt();

        paramName = reader.GetStringArray();
        paramType = reader.GetShortArray();
        paramValue = reader.GetFloatArray();
    }
}

public class EventData : INetSerializable
{
    public string lobbyHash { get; set; }
    public int photonId { get; set; }
    public string eventName { get; set; }
    public string data { get; set; }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(lobbyHash);
        writer.Put(photonId);
        writer.Put(eventName);
        writer.Put(data);
    }
    public void Deserialize(NetDataReader reader)
    {
        lobbyHash = reader.GetString();
        photonId = reader.GetInt();
        eventName = reader.GetString();
        data = reader.GetString();
    }
}