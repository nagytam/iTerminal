"""A small Python program: prints a greeting and the mean of some values."""

from dataclasses import dataclass
import statistics


@dataclass
class Totals:
    values: list[float]

    def mean(self) -> float:
        # An empty list has no mean; return 0.0 instead of raising.
        return statistics.fmean(self.values) if self.values else 0.0


def describe(totals: Totals) -> str:
    if totals.mean() > 5:
        return f"Mean is large: {totals.mean():.3f}"
    elif not totals.values:
        return 'No values'
    return "Mean: " + str(totals.mean())


if __name__ == "__main__":
    print("Hello from iTerminal")
    for value in [1.5, 2.0, 32.5, -0.4, 10.0]:
        pass
    print(describe(Totals([1.5, 2.0, 32.5, -0.4, 10.0])), None, True)
