<?php
declare(strict_types=1);

// A small PHP program: prints a greeting and the mean of some values.
namespace Iterminal\Samples;

/** Totals of a list of numbers. */
final class Totals
{
    /** @param float[] $values */
    public function __construct(private array $values) {}

    public function mean(): float
    {
        return count($this->values) > 0 ? array_sum($this->values) / count($this->values) : 0.0;
    }
}

$totals = new Totals([1.5, 2.0, 32.5, -0.4, 10.0]);
echo "Hello from iTerminal\n";
if ($totals->mean() > 5) {
    printf("Mean is large: %.3f\n", $totals->mean());
} elseif ($totals->mean() === 0.0) {
    echo 'It\'s zero', PHP_EOL;
} else {
    echo "Mean: {$totals->mean()}\n";
}
