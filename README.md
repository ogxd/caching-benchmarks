# Caching Benchmarks

This repository contains experimental implementations of various caching policies for benchmark purpose.   
Benchmark is done using 3 real world case datasets (CL, VCC, VDC) + data generated from theoritical distributions to test various behaviours.

## Candidates

Benchmarked caches are all supposed to be O(1) or near O(1).    
Implementations are not polished, what is benchmarked here is the algorithm itself and the policy efficiency given several scenario, and not the CPU time nor memory usage or thread safety (don't copy paste implementations as is, or at your own risk)

## Results

![Dataset Shared CL + VCC + VDC - efficiency](https://raw.githubusercontent.com/ogxd/caching-benchmarks/master/Results/DatasetSharedCLVCCVDC.png)
![Dataset CL - efficiency](https://raw.githubusercontent.com/ogxd/caching-benchmarks/master/Results/DatasetCL.png)
![Dataset VCC - efficiency](https://raw.githubusercontent.com/ogxd/caching-benchmarks/master/Results/DatasetVCC.png)
![Dataset VDC - efficiency](https://raw.githubusercontent.com/ogxd/caching-benchmarks/master/Results/DatasetVDC.png)
![Gaussian Bi-Modal - efficiency](https://raw.githubusercontent.com/ogxd/caching-benchmarks/master/Results/GaussianBiModal.png)
![Gaussian Switch Far - efficiency](https://raw.githubusercontent.com/ogxd/caching-benchmarks/master/Results/GaussianSwitchFar.png)
![Gaussian Switch Near - efficiency](https://raw.githubusercontent.com/ogxd/caching-benchmarks/master/Results/GaussianSwitchNear.png)
![Gaussian σ = 5K - efficiency](https://raw.githubusercontent.com/ogxd/caching-benchmarks/master/Results/Gaussian5K.png)
![Gaussian σ = 10K - efficiency](https://raw.githubusercontent.com/ogxd/caching-benchmarks/master/Results/Gaussian10K.png)
![Gaussian σ = 20K - efficiency](https://raw.githubusercontent.com/ogxd/caching-benchmarks/master/Results/Gaussian20K.png)
![Sparse 50K - efficiency](https://raw.githubusercontent.com/ogxd/caching-benchmarks/master/Results/Sparse50K.png)