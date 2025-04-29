namespace Onyx.Data.ApiSchema;

public class AppDto
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required byte[] IconBitmap { get; set; }
}

public class CreateAppDto
{
    public required string Name { get; set; }
    public required byte[] IconBitmap { get; set; }
}