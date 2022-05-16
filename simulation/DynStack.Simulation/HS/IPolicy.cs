using DynStack.DataModel.HS;
using System;
using System.Collections.Generic;
using System.Text;

namespace DynStack.Simulation.HS {
  public interface IPolicy {
    CraneSchedule GetSchedule(World world);
  }
}
