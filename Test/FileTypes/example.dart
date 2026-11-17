// A small Dart program: a greeting and the mean of some values.
import 'dart:math';

class Summary {
  final List<double> values;

  Summary(this.values);

  double get mean {
    if (values.isEmpty) return 0;
    return values.reduce((a, b) => a + b) / values.length;
  }
}

void main() {
  var summary = Summary([1.5, 2.0, 3.25e1, -4.0e-1, 10]);
  print("Hello from iTerminal");
  if (summary.mean > 5) {
    print('Mean is large: ${summary.mean}');
  } else {
    print('Mean is small');
  }
  /* A block comment
     over two lines. */
  final int mask = 0x1F;
  print(max(mask, 3));
}
