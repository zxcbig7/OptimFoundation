using OptimFoundation.Modeling;
using SandBox.SetClass;

namespace SandBox.Data
{
    [OptParam]
    [OptDim<Set_Employee>("Employee")]
    [OptDim<Set_Group>("Group")]
    public partial class Parameter_CrossGroup;
}
