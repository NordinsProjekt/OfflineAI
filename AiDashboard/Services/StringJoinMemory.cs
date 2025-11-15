using Entities;
using Services.Interfaces;
using System.Collections.Generic;

namespace AiDashboard.Services
{
    /// <summary>
    /// Simple in-memory storage for conversation history.
    /// </summary>
    public class StringJoinMemory : ILlmMemory
    {
        private readonly List<IMemoryFragment> _memory = new();

        public void ImportMemory(IMemoryFragment memoryFragment)
        {
            _memory.Add(memoryFragment);
        }

        public override string ToString()
        {
            return string.Join(System.Environment.NewLine, _memory);
        }
    }
}
