using FishNet.Transporting;

namespace FishyFacepunch
{

    [System.Serializable]
    public struct ChannelData
    {
        public Channel Channel;
        public int MaximumTransmissionUnit;

        public ChannelData(Channel channel)
        {
            Channel = channel;
            MaximumTransmissionUnit = 1200;
        }
        public ChannelData(Channel channel, int maximumTransmissionUnit)
        {
            Channel = channel;
            MaximumTransmissionUnit = maximumTransmissionUnit;
        }
    }

}