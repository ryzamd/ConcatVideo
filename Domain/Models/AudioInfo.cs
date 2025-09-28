namespace Domain.Models
{
    public class AudioInfo {
        public string? Codec;
        public int Channels;
        public int BitRate;
        public int SampleRate;

        public AudioInfo(string? code, int channels, int bitRate, int sampleRate)
        {
            Codec = code;
            Channels = channels;
            BitRate = bitRate;
            SampleRate = sampleRate;
        }
    }
}
