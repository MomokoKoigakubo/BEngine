#pragma once

#include<stdio.h>
#include<stdlib.h>
#include<string>
#include<map>
#include<variant>
#include<vector>

struct JsonValue;
using JsonArray = std::vector<JsonValue>;
using JsonObject = std::map<std::string, JsonValue>;

struct JsonValue {
	std::variant < std::nullptr_t, bool, double, std::string, JsonArray, JsonObject> data;
};

class JsonParser {
public:
	void ReadFile(const std::string& filePath, std::string& output);
	JsonValue parse(const std::string& text);
private:
	//text parsed
	const std::string* src = nullptr;
	//curser pos
	size_t i = 0;
	void skipWhiteSpace();
	char peek();
	char next();
	JsonValue parseValue();
	JsonValue parseObject();
	JsonValue parseArray();
	std::string parseString();
	JsonValue parseNumber();
};