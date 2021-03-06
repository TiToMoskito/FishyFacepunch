using Steamworks;
using System;

public class FishyConnectionManager : ConnectionManager
{
    public Action<IntPtr, int> ForwardMessage;

    public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
    {
        ForwardMessage(data, size);
    }
}