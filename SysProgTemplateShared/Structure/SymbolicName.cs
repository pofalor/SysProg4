using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysProgTemplateShared.Structure
{
    public class SymbolicName
    {
        public string Name { get; set; } = default!;
        public int Address { get; set; } = -1; 
        public List<int> AddressRequirements { get; set; } = []; 
    }
}
