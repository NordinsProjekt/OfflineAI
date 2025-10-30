using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MemoryLibrary.Models;
using Services;

namespace MemoryLibrary;

public class EntropiaUniverse : ILlmMemory
{
    private List<IMemoryFragment> Memory { get; set; } = new List<IMemoryFragment>
    {
        new MemoryFragment("Entropia Universe information", "Skill: Attributes: Agility\r\nSpecifications Category:\tAttributes\r\nHP increase:\t40 Levels\r\nHidden:\tNo\r\nAgility is a basic attribute available to all avatars. It influences almost every action where coordination, finesse and grace are involved.\r\n\r\n\r\nAgility is a very important attribute because it affects a wide variety of professions, increases your Health, and also increases your avatar's movement speed.\r\n\r\n\r\nProgress in Agility can be acquired both by attacking as well as being attacked.\r\n\r\n \r\n\r\nThe effect on speed diminishes exponentially after 60+ in Agility (source). For example:\r\n\r\n \r\n\r\nAgility\tSpeed, m/s\r\n5000 (an official)\r\n5.45\r\n140\t5.07\r\n100\t5.07\r\n80\t4.96\r\n60\t\r\n4.83\r\n\r\n50\t\r\n4.79\r\n\r\n \r\n\r\nSo a lowest level player using a cheap Hermetic ring can outrun an official.\r\n\r\n \r\n\r\nAnyone willing to test their speed can do it at Port Atlantis, there is a perfectly flat distance between [Calypso, 61327, 75262, 118, start] and [Calypso, 61693, 75262, 118, finish], use a stopwatch and then divide by 366 meters.\r\n\r\n"),
        new MemoryFragment("Entropia Universe information", "Skill: Attributes: Health Specifications Category:\tAttributes\r\nHidden:\tNo\r\nHealth is the amount of punishment your avatar can sustain before he or she dies.\r\n\r\n\r\nAvatars regenerate Health at a fixed rate of 4 hp per 20 seconds.\r\nThe regeneration rate is also not dependent on Stamina or any other attribute.\r\n\r\n \r\n\r\nThe starting Health value of a new avatar with just 1 in all skills and attributes is 88.18.\r\n\r\n \r\n\r\nHealth is calculated by summing minute contributions from an avatar's skills and attributes, and is therefore not gained in an integral fashion (not by +1 at a time). Each time an avatar gets a skill gain that influences Health a tiny fraction of Health is gained as well. A notice is given when the avatar Health reaches the next integer value.\r\n\r\n\r\nThe respective contributions of skills and attributes to Health can be found in the Skill Chart, where the column \"HP increase\" is the number of points of the skill or attribute needed to give one complete point of Health.")
    };

    public void ImportMemory(IMemoryFragment section)
    {
        Memory.Add(section);
    }

    public override string ToString()
    {
        return string.Join(Environment.NewLine, Memory);
    }
}
