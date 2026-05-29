#pragma once
#include <Volk/volk.h>

class Context
{
public:
	Context();
	~Context();
	Context(const Context&) = delete;
	Context& operator = (const Context&) = delete;
private:
	VkInstance instance;
	void createInstance();
};