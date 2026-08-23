using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SysProgTemplateShared.Helpers
{
    public static class Comparer
    {
        public static bool CompareSourceCodeVersions(List<List<string>> list1, List<List<string>> list2)
        {
            if (list1.Count != list2.Count)
            {
                return false;
            }

            for (int i = 0; i < list1.Count; i++)
            {
                var innerList1 = list1[i];
                var innerList2 = list2[i];

                if (!innerList1.SequenceEqual(innerList2))
                {
                    return false; 
                }
            }

            return true; 
        }
    }
}
