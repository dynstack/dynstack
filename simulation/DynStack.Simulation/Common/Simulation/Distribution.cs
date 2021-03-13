using SimSharp;

namespace DynStack.Simulation {
  public interface IDistribution<T> {
    T GetValue();
  }

  public class TriangularDistribution : IDistribution<double> {
    private SimSharp.Simulation _environment;
    private PcgRandom _rng;
    public double Low { get; private set; }
    public double High { get; private set; }
    public double Mode { get; private set; }

    public TriangularDistribution(SimSharp.Simulation environment, double low, double high, double mode) {
      _environment = environment;
      Low = low;
      High = high;
      Mode = mode;
    }
    public TriangularDistribution(SimSharp.Simulation environment, double low, double high, double mode, int seed) {
      _environment = environment;
      _rng = new PcgRandom(seed);
      Low = low;
      High = high;
      Mode = mode;
    }

    public double GetValue() {
      if (_rng == null) return _environment.RandTriangular(Low, High, Mode);
      return _environment.RandTriangular(_rng, Low, High, Mode);
    }
  }

  public class LognormalDistribution : IDistribution<double> {
    private SimSharp.Simulation _environment;
    private PcgRandom _rng;
    public double Mean { get; private set; }
    public double StdDev { get; private set; }

    public LognormalDistribution(SimSharp.Simulation environment, double mean, double std) {
      _environment = environment;
      Mean = mean;
      StdDev = std;
    }
    public LognormalDistribution(SimSharp.Simulation environment, double mean, double std, int seed) {
      _environment = environment;
      _rng = new PcgRandom(seed);
      Mean = mean;
      StdDev = std;
    }

    public double GetValue() {
      if (_rng == null) return _environment.RandLogNormal2(Mean, StdDev);
      return _environment.RandLogNormal2(_rng, Mean, StdDev);
    }
  }
}
