using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Services;

namespace MemoryLibrary.Models;

public class MemoryFragment : IMemoryFragment
{
    public string Category { get; set; }
    public string Content { get; set; }

    public MemoryFragment(string category, string content)
    {
        Category = category;
        Content = content;
    }

    public override string ToString()
    {
        return $"{Category}: {Content}";
    }
}