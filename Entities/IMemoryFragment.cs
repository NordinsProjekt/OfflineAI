namespace Entities;

public interface IMemoryFragment
{
    public string Category { get; set; }
    public string Content { get; set; }
    public string ToString();
}