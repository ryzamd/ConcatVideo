namespace Domain.Models
{
    public class AudioInfo {
        public string? Codec;
        public int Channels;
        public int BitRate;
    
        public AudioInfo(string? code, int channels, int bitRate)
        {
            Codec = code;
            Channels = channels;
            BitRate = bitRate; 
        }
    }
}
