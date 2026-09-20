# Detailed matched benchmark results

Ratios are PersistentPieceTree / PieceTree. Times are microseconds per operation; edit batches count as one operation. Allocation is bytes per operation. See piece-tree-performance.md for validity, warmup and cache caveats.

| Method / parameters | PieceTree us | Persistent us | Time ratio | PieceTree B | Persistent B | CV old / new | Job |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| ApplyEditsAfterEdits (BilingualDictionary) | 1326.124490 | 9094.962963 | 6.858 | 969968 | 15259200 | 3.9% / 2.8% | Default+150warmups |
| ApplyEditsAfterEdits (CheckerTs) | 1112.374286 | 8815.052381 | 7.925 | 971432 | 15280224 | 2.7% / 2.3% | Default+150warmups |
| ApplyEditsAfterEdits (SqliteC) | 1181.174444 | 8429.715385 | 7.137 | 970288 | 15259776 | 3.8% / 1.0% | Default+150warmups |
| ApplyRandomEdits (BilingualDictionary, 100) | 145.523077 | 562.316667 | 3.864 | 94152 | 960480 | 0.6% / 0.5% | Default+150warmups |
| ApplyRandomEdits (BilingualDictionary, 1000) | 1290.035526 | 8082.783721 | 6.266 | 969448 | 13237920 | 5.1% / 3.4% | Default+150warmups |
| ApplyRandomEdits (CheckerTs, 100) | 101.338462 | 620.756566 | 6.126 | 94152 | 960480 | 2.6% / 15.1% | Default+150warmups |
| ApplyRandomEdits (CheckerTs, 1000) | 982.347368 | 7713.864286 | 7.852 | 969336 | 13228800 | 2.1% / 1.4% | Default+150warmups |
| ApplyRandomEdits (SqliteC, 100) | 111.557471 | 551.950000 | 4.948 | 94152 | 960480 | 14.5% / 1.4% | Default+150warmups |
| ApplyRandomEdits (SqliteC, 1000) | 1064.788679 | 7905.825000 | 7.425 | 969448 | 13237920 | 4.0% / 2.8% | Default+150warmups |
| ApplySequentialEdits (BilingualDictionary, 100) | 26.039394 | 26.961538 | 1.035 | 43160 | 26304 | 3.2% / 1.2% | Default+150warmups |
| ApplySequentialEdits (BilingualDictionary, 1000) | 180.231250 | 201.534615 | 1.118 | 428416 | 199104 | 1.3% / 0.6% | Default+150warmups |
| ApplySequentialEdits (CheckerTs, 100) | 25.693182 | 38.660000 | 1.505 | 43160 | 26304 | 9.1% / 10.0% | Default+150warmups |
| ApplySequentialEdits (CheckerTs, 1000) | 177.200000 | 287.007692 | 1.620 | 428416 | 199104 | 1.3% / 0.9% | Default+150warmups |
| ApplySequentialEdits (SqliteC, 100) | 24.634615 | 30.165625 | 1.225 | 43160 | 26304 | 1.5% / 3.1% | Default+150warmups |
| ApplySequentialEdits (SqliteC, 1000) | 175.433333 | 242.692857 | 1.383 | 428416 | 199104 | 0.6% / 0.5% | Default+150warmups |
| ApplySingleEdit (BilingualDictionary) | 7.243820 | 8.827660 | 1.219 | 960 | 8736 | 7.9% / 6.7% | Default+150warmups |
| ApplySingleEdit (CheckerTs) | 9.606000 | 10.241837 | 1.066 | 960 | 8736 | 44.5% / 16.6% | Default+150warmups |
| ApplySingleEdit (SqliteC) | 6.369565 | 9.842391 | 1.545 | 960 | 8736 | 3.8% / 7.9% | Default+150warmups |
| CreateSnapshot (BilingualDictionary, 0) | 0.046142 | 0.001013 | 0.022 | 184 | 0 | 3.4% / 0.4% | ShortRun |
| CreateSnapshot (BilingualDictionary, 1000) | 34.140140 | 0.000775 | 0.000 | 96136 | 0 | 1.5% / 0.7% | ShortRun |
| CreateSnapshot (BilingualDictionary, 2000) | 79.794881 | 0.000871 | 0.000 | 192040 | 0 | 1.7% / 13.6% | ShortRun |
| CreateSnapshot (CheckerTs, 0) | 0.042934 | 0.001046 | 0.024 | 184 | 0 | 5.1% / 0.4% | ShortRun |
| CreateSnapshot (CheckerTs, 1000) | 32.069777 | 0.000777 | 0.000 | 96088 | 0 | 0.5% / 0.9% | ShortRun |
| CreateSnapshot (CheckerTs, 2000) | 80.623584 | 0.000764 | 0.000 | 191704 | 0 | 0.9% / 1.8% | ShortRun |
| CreateSnapshot (SqliteC, 0) | 0.046909 | 0.001033 | 0.022 | 184 | 0 | 0.7% / 1.4% | ShortRun |
| CreateSnapshot (SqliteC, 1000) | 37.414388 | 0.000807 | 0.000 | 96136 | 0 | 6.8% / 2.5% | ShortRun |
| CreateSnapshot (SqliteC, 2000) | 84.447913 | 0.000753 | 0.000 | 191992 | 0 | 2.1% / 1.9% | ShortRun |
| EditAndRetainEveryVersion (100, False) | 305.593099 | 88.329032 | 0.289 | 539869 | 112608 | 0.3% / 0.3% | ShortRun |
| EditAndRetainEveryVersion (100, True) | 196.317782 | 60.512270 | 0.308 | 268157 | 36096 | 0.2% / 3.2% | ShortRun |
| EditAndRetainEveryVersion (1000, False) | 53013.240741 | 806.448177 | 0.015 | 25189609 | 1542816 | 3.3% / 0.2% | ShortRun |
| EditAndRetainEveryVersion (1000, True) | 476.189079 | 168.224341 | 0.353 | 869413 | 302496 | 1.7% / 0.7% | ShortRun |
| LoadFile (BilingualDictionary) | 43534.394444 | 37326.621429 | 0.857 | 52209257 | 52190920 | 2.1% / 2.5% | ShortRun |
| LoadFile (CheckerTs) | 7952.312760 | 6198.199870 | 0.779 | 13401109 | 13382927 | 4.7% / 4.5% | ShortRun |
| LoadFile (SqliteC) | 14610.919792 | 13232.055208 | 0.906 | 24329750 | 24311590 | 1.4% / 2.1% | ShortRun |
| ReadLine (BilingualDictionary) | 5.278022 | 6.644737 | 1.259 | 176 | 208 | 41.9% / 37.5% | Default |
| ReadLine (CheckerTs) | 6.563333 | 5.338889 | 0.813 | 152 | 168 | 22.8% / 35.8% | Default |
| ReadLine (SqliteC) | 5.746739 | 6.117021 | 1.064 | 160 | 176 | 30.8% / 22.7% | Default |
| ReadLineAfter1000RandomEdits (BilingualDictionary) | 4.336735 | 4.452577 | 1.027 | 80 | 136 | 33.7% / 35.7% | Default |
| ReadLineAfter1000RandomEdits (CheckerTs) | 2.517582 | 3.008081 | 1.195 | 280 | 528 | 8.1% / 8.5% | Default+150warmups |
| ReadLineAfter1000RandomEdits (SqliteC) | 4.551613 | 5.387113 | 1.184 | 72 | 112 | 28.4% / 35.6% | Default |
| ReadLineAfter1000SequentialEdits (BilingualDictionary) | 4.084848 | 5.831000 | 1.427 | 120 | 208 | 40.6% / 35.5% | Default |
| ReadLineAfter1000SequentialEdits (CheckerTs) | 4.888421 | 4.665625 | 0.954 | 96 | 168 | 14.8% / 23.6% | Default |
| ReadLineAfter1000SequentialEdits (SqliteC) | 4.945833 | 5.691489 | 1.151 | 104 | 176 | 21.6% / 12.1% | Default |
| ReadLineAfterSingleEdit (BilingualDictionary) | 4.193684 | 5.517708 | 1.316 | 120 | 208 | 34.7% / 30.2% | Default |
| ReadLineAfterSingleEdit (CheckerTs) | 5.132222 | 4.660417 | 0.908 | 96 | 168 | 15.9% / 19.2% | Default |
| ReadLineAfterSingleEdit (SqliteC) | 5.916667 | 4.533333 | 0.766 | 104 | 176 | 7.6% / 5.1% | ShortRun |
| ReadLineSteadyState (BilingualDictionary, None) | 0.001675 | 0.077071 | 46.000 | 0 | 208 | 0.4% / 1.4% | ShortRun |
| ReadLineSteadyState (BilingualDictionary, RandomEdits) | 0.001830 | 0.156728 | 85.663 | 0 | 136 | 1.3% / 3.3% | ShortRun |
| ReadLineSteadyState (BilingualDictionary, SequentialEdits) | 0.002533 | 0.084742 | 33.455 | 0 | 208 | 6.2% / 5.0% | ShortRun |
| ReadLineSteadyState (BilingualDictionary, SingleEdit) | 0.002221 | 0.089723 | 40.403 | 0 | 208 | 2.1% / 3.4% | ShortRun |
| ReadLineSteadyState (CheckerTs, None) | 0.001770 | 0.091711 | 51.820 | 0 | 168 | 0.4% / 0.1% | ShortRun |
| ReadLineSteadyState (CheckerTs, RandomEdits) | 0.001828 | 0.193559 | 105.874 | 0 | 528 | 2.1% / 0.2% | ShortRun |
| ReadLineSteadyState (CheckerTs, SequentialEdits) | 0.002489 | 0.108494 | 43.595 | 0 | 168 | 6.0% / 1.8% | ShortRun |
| ReadLineSteadyState (CheckerTs, SingleEdit) | 0.001923 | 0.097934 | 50.920 | 0 | 168 | 0.8% / 0.5% | ShortRun |
| ReadLineSteadyState (SqliteC, None) | 0.001970 | 0.072709 | 36.917 | 0 | 176 | 0.5% / 0.4% | ShortRun |
| ReadLineSteadyState (SqliteC, RandomEdits) | 0.001797 | 0.142913 | 79.547 | 0 | 112 | 0.9% / 0.2% | ShortRun |
| ReadLineSteadyState (SqliteC, SequentialEdits) | 0.002115 | 0.087583 | 41.406 | 0 | 176 | 0.3% / 0.1% | ShortRun |
| ReadLineSteadyState (SqliteC, SingleEdit) | 0.002010 | 0.081780 | 40.686 | 0 | 176 | 0.1% / 0.1% | ShortRun |
