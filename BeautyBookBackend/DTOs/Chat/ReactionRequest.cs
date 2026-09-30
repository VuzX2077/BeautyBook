namespace BeautyBookBackend.DTOs.Chat
{
    public class ReactionRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MaxLength(16)]
        public string Emoji { get; set; } = "❤️";
    }
}
