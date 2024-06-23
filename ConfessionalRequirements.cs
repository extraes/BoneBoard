using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard;

[Flags]
internal enum ConfessionalRequirements
{
    NONE,
    ROLE = 1 << 0,
    COOLDOWN = 1 << 1,
    ROLE_AND_COOLDOWN = ROLE | COOLDOWN,
}
