#include <fstream>
#include <string>
#include <cctype>
#include "jparser.h"

void JsonParser::ReadFile(const std::string& filePath, std::string& output) {
	std::ifstream file(filePath);
	std::string line;

	while (std::getline(file, line))
	{
		output.append(line);
	}

}

JsonValue JsonParser::parse(const std::string& text)
{
	src = &text;
	i = 0;
	return parseValue();
}

char JsonParser::peek() 
{
	return(*src)[i];
}

char JsonParser::next() 
{
	return(*src)[i++];
}

void JsonParser::skipWhiteSpace()
{
	while (i < src->size() &&
		(peek() == ' ' || peek() == '\t' || peek() == '\n' || peek() == '\r'))
	{
		i++;
	}
}

std::string JsonParser::parseString()
{
	std::string result;
	next();								// consume the opening "

	while (peek() != '"')				// read until the closing quote
	{
		char c = next();				// consume one character
		if (c == '\\')					// escape sequence: the next char is escaped
		{
			char e = next();
			switch (e)
			{
				case '"':  result += '"';  break;
				case '\\': result += '\\'; break;
				case '/':  result += '/';  break;
				case 'n':  result += '\n'; break;
				case 't':  result += '\t'; break;
				case 'r':  result += '\r'; break;
				case 'b':  result += '\b'; break;
				case 'f':  result += '\f'; break;
				default:   result += e;    break;	// unknown escape: keep as-is
			}
		}
		else
		{
			result += c;				// normal character
		}
	}

	next();								// consume the closing "
	return result;
}

JsonValue JsonParser::parseNumber()
{
	size_t start = i;					
	while (i < src->size() &&
		(std::isdigit((unsigned char)peek()) ||
		 peek() == '-' || peek() == '+' ||
		 peek() == '.' || peek() == 'e' || peek() == 'E'))
	{
		i++;							
	}
	double d = std::stod(src->substr(start, i - start));
	return JsonValue{ d };				
}

JsonValue JsonParser::parseValue() 
{
	skipWhiteSpace();
	char c = peek();
	
	if (c == '{') return parseObject();
	if (c == '[') return parseArray();
	if (c == '"') return JsonValue{ parseString() };
	if (c == 't') { i += 4; return JsonValue{ true }; }		// "true"
	if (c == 'f') { i += 5; return JsonValue{ false }; }	// "false"
	if (c == 'n') { i += 4; return JsonValue{ nullptr }; }	// "null"

	return parseNumber();									// digit or '-'
}

JsonValue JsonParser::parseArray()
{
	JsonArray arr;
	next();									// consume '['
	skipWhiteSpace();

	if (peek() == ']') { next(); return JsonValue{ arr }; }	// empty []

	while (true)
	{
		arr.push_back(parseValue());		// parse one element
		skipWhiteSpace();
		if (peek() == ',') { next(); continue; }	// more elements coming
		if (peek() == ']') { next(); break; }		// end of array
	}
	return JsonValue{ arr };
}

JsonValue JsonParser::parseObject()
{
	JsonObject obj;
	next();									// consume '{'
	skipWhiteSpace();

	if (peek() == '}') { next(); return JsonValue{ obj }; }	// empty {}

	while (true)
	{
		skipWhiteSpace();
		std::string key = parseString();	// keys are always strings
		skipWhiteSpace();
		next();								// consume ':'
		obj[key] = parseValue();			// parse the value
		skipWhiteSpace();
		if (peek() == ',') { next(); continue; }	// more pairs coming
		if (peek() == '}') { next(); break; }		// end of object
	}
	return JsonValue{ obj };
}