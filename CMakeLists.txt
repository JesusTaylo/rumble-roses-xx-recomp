# rrxx - ReXGlue Recompiled Project
#
# SDK boilerplate lives in generated/rexglue.cmake.

cmake_minimum_required(VERSION 3.25)
project(rrxx LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(generated/rexglue.cmake)

# Sources
set(RRXX_SOURCES
    src/main.cpp
    src/crash_handler.cpp
)

if(WIN32)
    # Icono del exe y de la ventana (resources/rrxx.rc)
    enable_language(RC)
    list(APPEND RRXX_SOURCES resources/rrxx.rc)
    add_executable(rrxx WIN32 ${RRXX_SOURCES})
else()
    add_executable(rrxx ${RRXX_SOURCES})
endif()

rexglue_setup_target(rrxx GPU_PLUGINS xenos)

if(WIN32)
    # crash_handler.cpp: minidump + pila simbolizada
    target_link_libraries(rrxx PRIVATE dbghelp)
endif()
