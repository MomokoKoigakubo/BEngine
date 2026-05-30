#pragma once
#include <vector>
#include <string>

namespace fileio 
{
	std::vector<char> readBinary(const std::string& path);
}