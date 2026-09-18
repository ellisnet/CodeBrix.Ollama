# Reports what the embedded interpreter actually is.
#
# It is the smallest thing this library ever asks Python to do, and it is asked as soon as an
# interpreter has started: the answer says which CPython is running, which prefix it runs out of -
# a virtual environment's, when one is in effect - and where its executable is. The only import is
# 'sys', which is part of every interpreter and needs nothing installed.
import sys

result = {
    "major": sys.version_info.major,
    "minor": sys.version_info.minor,
    "micro": sys.version_info.micro,
    "prefix": sys.prefix,
    "base_prefix": sys.base_prefix,
    "executable": sys.executable,
}
