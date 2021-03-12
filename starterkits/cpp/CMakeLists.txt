cmake_minimum_required(VERSION 3.19)

if(DEFINED ENV{VCPKG_ROOT} AND NOT DEFINED CMAKE_TOOLCHAIN_FILE)
  set(CMAKE_TOOLCHAIN_FILE "$ENV{VCPKG_ROOT}/scripts/buildsystems/vcpkg.cmake"
    CACHE STRING "")
    message(" [INFO] VCPKG CMAKE_TOOLCHAIN_FILE = ${CMAKE_TOOLCHAIN_FILE}")
endif()


set(CMAKE_CXX_STANDARD 17) 

project(dynstack)

include(FindProtobuf)
find_package(Protobuf REQUIRED)
include_directories(${PROTOBUF_INCLUDE_DIR})
find_package(cppzmq CONFIG REQUIRED)


add_executable(stacking
  "src/stacking.cc"
  "src/hotstorage/heuristic.h"
  "src/hotstorage/heuristic.cc"
  "src/hotstorage/hotstorage_model.pb.cc"
  "src/hotstorage/hotstorage_model.pb.h"
  "src/rollingmill/heuristic.h"
  "src/rollingmill/heuristic.cc"
  "src/rollingmill/rollingmill_model.pb.cc"
  "src/rollingmill/rollingmill_model.pb.h"
  )
target_link_libraries(stacking PRIVATE protobuf::libprotoc protobuf::libprotobuf protobuf::libprotobuf-lite)
target_link_libraries(stacking PRIVATE cppzmq cppzmq-static)