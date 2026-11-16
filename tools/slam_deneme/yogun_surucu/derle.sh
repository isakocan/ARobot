# WSL (root): /root/slam_dense/surucu altında derler. Kütüphane: /root/slam_dense/stella_vslam_dense/build/3rd/patch_match_dense/libPatchMatchDense.so
D=/root/slam_dense/stella_vslam_dense
g++ -std=c++17 -O2 -I$D/3rd/patch_match_dense/src -I/usr/local/cuda-12.9/include -I/usr/include/opencv4 surucu.cc -o surucu \
  -L$D/build/3rd/patch_match_dense -lPatchMatchDense -L/usr/local/cuda-12.9/lib64 -lcudart -lopencv_core -lopencv_imgproc -lopencv_imgcodecs -lpthread
