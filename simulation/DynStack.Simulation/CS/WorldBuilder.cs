using System.Collections.Generic;
using DynStack.DataModel.CS;

namespace DynStack.Simulation.CS {
  public static class WorldBuilder {
    public static World Build(int height, int width,
      IEnumerable<(double pos, int maxheight)> locations,
      IEnumerable<(double pos, double width)> cranes) {

      var world = new World() {
        Height = height,
        Width = width,
        Now = new DataModel.TimeStamp(0),
        Locations = new List<Location>(),
        Cranes = new List<Crane>(),
        CraneMoves = new List<CraneMove>(),
        CraneSchedule = new CraneSchedule()
      };

      var locationId = 0;
      foreach (var loc in locations) {
        ++locationId;
        world.Locations.Add(new Location() {
          Id = locationId,
          GirderPosition = loc.pos,
          MaxHeight = loc.maxheight
        });
      }

      var craneId = 0;
      var widthSum = 0.0;
      foreach (var crane in cranes) {
        ++craneId;
        world.Cranes.Add(new Crane() {
          Id = craneId,
          CraneCapacity = 1,
          HoistLevel = height,
          GirderPosition = crane.pos,
          Width = crane.width,
          MinPosition = widthSum + crane.width / 2
        });
        widthSum += crane.width;
      }
      for (var c = 0; c < world.Cranes.Count; c++) {
        world.Cranes[c].MaxPosition = world.Width - widthSum + world.Cranes[c].Width / 2;
        widthSum -= world.Cranes[c].Width;
      }
      return world;
    }
  }
}
