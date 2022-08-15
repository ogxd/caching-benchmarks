# Caching Benchmarks

This repository contains experimental implementations of various caching policies for benchmark purpose

## Results

|                | A   | B   | C   | D  | E  | F |
|----------------|-----|-----|-----|----|----|---|
| LRU            | 30% | 58% | 96% | 43 |    |   |
| LU             | 33% | 64% | 96% | 49 | 25 | 6 |
| LFU bruteforce | 36% | 68% | 96% | 53 |    |   |
| LFU            | 36% | 68% | 96% | 53 |    |   |
| LFURA          | 34% | 68% | 96% | 52 |    |   |