// JunexisLang Grammar — specification 0.2 Experimental
// ANTLR4
//
// Spec: spec/

grammar JunexisLang;

// ═══════════════════════════════════════════════════════════════════
// PARSER RULES
// ═══════════════════════════════════════════════════════════════════

// ─────────────────── Program ────────────────────────────────────────

program
    : importDeclaration* topLevelDeclaration* EOF
    ;

importDeclaration
    : IMPORT modulePath (AS IDENTIFIER)? SEMICOLON?              // import m::p as alias
    | IMPORT modulePath LBRACE importSymbolList RBRACE SEMICOLON? // import m::p { X, Y }
    ;

importSymbolList
    : importSymbol (COMMA importSymbol)* COMMA?
    ;

importSymbol
    : anyIdentifier (AS anyIdentifier)?                           // X as alias
    ;

modulePath
    : IDENTIFIER (DOUBLE_COLON IDENTIFIER)*
    ;

// ─────────────────── Top-level declarations ─────────────────────────

topLevelDeclaration
    : taskDeclaration
    | externDeclaration             // EXPERIMENTAL (D13) — FFI bridge do kodu natywnego
    | externTypeDeclaration          // EXPERIMENTAL (D13) — opaque handle na typ natywny
    | externContractDeclaration      // EXPERIMENTAL (D14) — named integration boundary
    | storageDeclaration             // EXPERIMENTAL (D22) — logical model + query intent
    | typeDeclaration
    | deviceDeclaration
    | narrativeDeclaration
    | pipelineDeclaration
    | deploymentDeclaration
    | supervisorDeclaration
    | fnDeclaration
    | quirksDeclaration
    | circuitBreakerDeclaration
    | protocolDeclaration
    ;

// ─────────────────── extern (EXPERIMENTAL: D13) ───────────────────────

externDeclaration
    : EXTERN TASK IDENTIFIER
      (LPAREN parameterList? RPAREN)?
      platformAnnotation*
      communicationSignature?
      SEMICOLON?
    ;

externTypeDeclaration
    : EXTERN TYPE IDENTIFIER platformAnnotation+ SEMICOLON?
    ;

// ───────────── storage (EXPERIMENTAL: D22/D35, M13/M14) ─────────────

storageDeclaration
    : STORAGE anyIdentifier LBRACE storageMember* RBRACE
    ;

storageMember
    : storageEntityDeclaration
    | storageQueryDeclaration
    | storageMutationDeclaration
    ;

storageEntityDeclaration
    : ENTITY anyIdentifier LBRACE storageEntityMember* RBRACE
    ;

storageEntityMember
    : storageModelField
    | storagePrimaryKey
    ;

storageModelField
    : anyIdentifier COLON typeReference SEMICOLON?
    ;

storagePrimaryKey
    : KEY LPAREN anyIdentifier (COMMA anyIdentifier)* RPAREN SEMICOLON?
    ;

storageQueryDeclaration
    : QUERY anyIdentifier LPAREN storageQueryParameterList? RPAREN
      FROM anyIdentifier LBRACE storageQueryStep* RBRACE
    ;

storageQueryParameterList
    : storageQueryParameter (COMMA storageQueryParameter)*
    ;

storageQueryParameter
    : anyIdentifier COLON typeReference
    ;

storageQueryStep
    : storageFilterStep
    | storageProjectStep
    | storageSortStep
    | storageLimitStep
    ;

storageFilterStep
    : FILTER expression SEMICOLON?
    ;

storageProjectStep
    : PROJECT storageProjection (COMMA storageProjection)* SEMICOLON?
    ;

storageProjection
    : anyIdentifier (AS anyIdentifier)?
    ;

storageSortStep
    : SORT storageSortKey (COMMA storageSortKey)* SEMICOLON?
    ;

storageSortKey
    : anyIdentifier (ASC | DESC)?
    ;

storageLimitStep
    : LIMIT INTEGER_LITERAL (OFFSET INTEGER_LITERAL)? SEMICOLON?
    ;

storageMutationDeclaration
    : MUTATION anyIdentifier LPAREN storageQueryParameterList? RPAREN
      storageMutationKind storageMutationCardinality anyIdentifier
      LBRACE storageMutationAssignment* storageMutationWhere? RBRACE
    ;

storageMutationKind
    : INSERT
    | UPDATE
    | DELETE
    ;

storageMutationCardinality
    : SINGLE
    | MANY
    ;

storageMutationAssignment
    : SET anyIdentifier ASSIGN expression SEMICOLON?
    ;

storageMutationWhere
    : WHERE expression SEMICOLON?
    ;

externContractDeclaration
    : EXTERN anyIdentifier anyIdentifier externContractBody
    ;

externContractBody
    : LBRACE externContractMember* RBRACE
    ;

externContractMember
    : externFunctionMember
    | externFileMember
    | externLanguageMember
    | externTargetMember
    ;

externTargetMember
    : IDENTIFIER platformTarget SEMICOLON?
    ;

externLanguageMember
    : IDENTIFIER IDENTIFIER SEMICOLON?
    ;

externFileMember
    : IDENTIFIER STRING_LITERAL SEMICOLON?
    ;

externFunctionMember
    : FN IDENTIFIER LPAREN parameterList? RPAREN (ARROW typeReference)? SEMICOLON?
    ;

// ─────────────────── task ───────────────────────────────────────────
// CORE — ustrukturyzowane

taskDeclaration
    : TASK IDENTIFIER
      (LPAREN parameterList? RPAREN)?
      platformAnnotation*
      communicationSignature?
      taskBody
    ;

communicationSignature
    : ARROW typeReference
    ;

taskBody
    : LBRACE taskMember* RBRACE
    ;

taskMember
    : stateDeclaration
    | configBlock
    | nativeSection                  // embedded library deps — spójne z device.native (D13)
    | lifecycleHook
    | statement
    ;

// ─────────────────── struct / enum ──────────────────────────────────
// CORE

typeDeclaration
    : structDeclaration
    | enumDeclaration
    ;

structDeclaration
    : STRUCT IDENTIFIER LBRACE structField* RBRACE
    ;

structField
    : IDENTIFIER COLON typeReference (ASSIGN expression)? (SEMICOLON | COMMA)?
    ;

enumDeclaration
    : ENUM IDENTIFIER (LT IDENTIFIER (COMMA IDENTIFIER)* GT)? LBRACE enumVariant (COMMA enumVariant)* COMMA? RBRACE
    ;

enumVariant
    : IDENTIFIER (LPAREN typeReference (COMMA typeReference)* RPAREN)?
    ;

// ─────────────────── fn ──────────────────────────────────────────────
// CORE

fnDeclaration
    : FN IDENTIFIER LPAREN parameterList? RPAREN (ARROW typeReference)? fnBody
    ;

fnBody
    : LBRACE statement* RBRACE
    ;

// ─────────────────── pipeline ────────────────────────────────────────
// CORE

pipelineDeclaration
    : PIPELINE IDENTIFIER LBRACE pipelineConnection* RBRACE
    ;

pipelineConnection
    : USE pipelineSource (ARROW pipelineTarget)? SEMICOLON?
    | PARALLEL LBRACE pipelineConnection+ RBRACE
    ;

pipelineSource
    : IDENTIFIER (DOT IDENTIFIER)?
    ;

pipelineTarget
    : IDENTIFIER (DOT IDENTIFIER)?
    ;

// ─────────────────── device ──────────────────────────────────────────
// CORE

deviceDeclaration
    : DEVICE IDENTIFIER platformAnnotation LBRACE deviceMember* RBRACE
    ;

deviceMember
    : hardwareBlock
    | nativeSection
    | configBlock
    | networkBlock
    | timingBlock
    ;

hardwareBlock
    : IDENTIFIER LBRACE deviceField* RBRACE
    ;

networkBlock
    : IDENTIFIER LBRACE deviceField* RBRACE
    ;

timingBlock
    : IDENTIFIER LBRACE deviceField* RBRACE
    ;

deviceField
    : IDENTIFIER COLON typeReference (COLON literal)? SEMICOLON?
    ;

// ─────────────────── native ─────────────────────────────────────────
// CORE — sekcja zależności natywnych wewnątrz device lub task

nativeSection
    : NATIVE LBRACE nativeLanguage* RBRACE
    ;

nativeLanguage
    : AT IDENTIFIER LBRACE nativeField* RBRACE
    ;

nativeField
    : nativeFieldName COLON nativeValue SEMICOLON?
    ;

// Nazwy pól natywnych mogą być keywordem JunexisLang (np. use, import).
nativeFieldName
    : IDENTIFIER | USE | IMPORT
    ;

nativeValue
    : STRING_LITERAL
    | RAW_STRING
    | TRIPLE_STRING
    | codeBlock
    | nativeFieldList
    ;

nativeFieldList
    : LBRACKET STRING_LITERAL (COMMA STRING_LITERAL)* RBRACKET
    ;

codeBlock
    : IDENTIFIER LBRACE codeBlockContent RBRACE
    ;

codeBlockContent
    : (~(LBRACE | RBRACE) | LBRACE codeBlockContent RBRACE)*
    ;

// ─────────────────── config ────────────────────────────────────────
// CORE

configBlock
    : CONFIG LBRACE configField* RBRACE
    ;

configField
    : IDENTIFIER COLON typeReference (COLON literal)? (SEMICOLON | COMMA)?
    ;

// ─────────────────── lifecycle hooks ─────────────────────────────────
// CORE

lifecycleHook
    : ON IDENTIFIER LBRACE statement* RBRACE
    ;

// ─────────────────── state ────────────────────────────────────────────
// CORE

stateDeclaration
    : STATE IDENTIFIER COLON typeReference (ASSIGN expression)? SEMICOLON?
    ;

// ─────────────────── Protocol binding (D7) ─────────────────────────────
// CORE — top-level deklaracja bindingu taska z protokołem.
// Nazwa protokołu to IDENTIFIER walidowany przez IR stage, nie przez parser.
// Ciało jest opaque — parser zbiera balanced braces.

protocolDeclaration
    : PROTOCOL IDENTIFIER protocolBody
    ;

protocolBody
    : LBRACE protocolContent* RBRACE
    ;

protocolContent
    : protocolBody
    | ~(LBRACE | RBRACE)
    ;

// ─────────────────── Experimental opaque declarations (D3) ────────────
// EXPERIMENTAL — parser zbiera balanced braces, nie analizuje treści

narrativeDeclaration
    : NARRATIVE (STRING_LITERAL | IDENTIFIER) narrativeBody
    ;

narrativeBody
    : LBRACE narrativeContent* RBRACE
    ;

narrativeContent
    : narrativeBody
    | ~(LBRACE | RBRACE)
    ;

deploymentDeclaration
    : DEPLOYMENT (STRING_LITERAL | IDENTIFIER)? deploymentBody
    ;

deploymentBody
    : LBRACE deploymentContent* RBRACE
    ;

deploymentContent
    : deploymentBody
    | ~(LBRACE | RBRACE)
    ;

supervisorDeclaration
    : SUPERVISOR IDENTIFIER supervisorBody
    ;

supervisorBody
    : LBRACE supervisorContent* RBRACE
    ;

supervisorContent
    : supervisorBody
    | ~(LBRACE | RBRACE)
    ;

quirksDeclaration
    : QUIRKS (STRING_LITERAL | IDENTIFIER) quirksBody
    ;

quirksBody
    : LBRACE quirksContent* RBRACE
    ;

quirksContent
    : quirksBody
    | ~(LBRACE | RBRACE)
    ;

circuitBreakerDeclaration
    : CIRCUIT_BREAKER IDENTIFIER circuitBreakerBody
    ;

circuitBreakerBody
    : LBRACE circuitBreakerContent* RBRACE
    ;

circuitBreakerContent
    : circuitBreakerBody
    | ~(LBRACE | RBRACE)
    ;

// ─────────────────── Types ────────────────────────────────────────────

// Union: A | B | C — lista atomicType rozdzielona PIPE.
// Brak osobnej reguły unionType — eliminuje wzajemną lewą rekurencję.
typeReference
    : atomicType (PIPE atomicType)*
    ;

atomicType
    : communicationType
    | mapType
    | genericType
    | arrayType
    | baseType
    ;

baseType
    : INT_TYPE
    | FLOAT_TYPE
    | BOOL_TYPE
    | TEXT_TYPE
    | SYMBOL_TYPE
    | TIME_TYPE
    | DURATION_TYPE
    | IDENTIFIER          // user-defined types + sized types (u8, u32, f64 ...)
    ;

communicationType
    : CHAN    LT typeReference GT
    | QUEUE   LT typeReference GT
    | STREAM  LT typeReference GT
    ;

mapType
    : MAP_TYPE LT typeReference COMMA typeReference GT
    ;

genericType
    : IDENTIFIER LT typeReference (COMMA typeReference)* GT
    ;

arrayType
    : LBRACKET typeReference RBRACKET
    ;

// ─────────────────── Platform annotations ────────────────────────────

platformAnnotation
    : AT platformTarget (LPAREN platformArgument (COMMA platformArgument)* RPAREN)?
    ;

platformTarget
    : EMBEDDED
    | SERVER
    | REPORT
    | EDGE
    | CLOUD_SERVER
    | MOBILE
    | DESKTOP
    | WEB
    | MICROCONTROLLER
    | FOG_NODE
    | DATA_CENTER
    | IDENTIFIER // Forward-compatible: nowe platformy bez zmiany gramatyki
    ;

platformArgument
    : anyIdentifier ASSIGN platformValue // klucz=wartość
    | platformValue                      // positional
    ;

platformValue
    : literal
    | anyIdentifier
    ;

// ─────────────────── Parameters ──────────────────────────────────────

parameterList
    : parameter (COMMA parameter)*
    ;

parameter
    : anyIdentifier COLON typeReference (ASSIGN expression)?
    ;

// ─────────────────── Statements ──────────────────────────────────────

statement
    : communicationStatement
    | expressionStatement
    | declarationStatement
    | controlStatement
    | logStatement
    | returnStatement
    | delayStatement
    | matchStatement
    | stateDeclaration
    | SEMICOLON  // empty statement
    ;

expressionStatement
    : expression SEMICOLON?
    ;

declarationStatement
    : (LET | CONST | VAR) IDENTIFIER (COLON typeReference)? ASSIGN expression SEMICOLON?
    ;

returnStatement
    : RETURN expression? SEMICOLON?
    ;

delayStatement
    : DELAY expression SEMICOLON?
    ;

logStatement
    : LOG LPAREN expression (COMMA expression)* RPAREN SEMICOLON?
    ;

// ─────────────────── Control flow ─────────────────────────────────────

controlStatement
    : ifStatement
    | forStatement
    | everyStatement
    | loopStatement
    ;

loopStatement
    : LOOP LBRACE statement* RBRACE
    ;

ifStatement
    : IF expression LBRACE statement* RBRACE
      (ELSE (ifStatement | LBRACE statement* RBRACE))?
    ;

forStatement
    : FOR anyIdentifier IN expression LBRACE statement* RBRACE
    ;

everyStatement
    : EVERY expression LBRACE statement* RBRACE
    ;

matchStatement
    : MATCH expression LBRACE matchArm+ RBRACE
    ;

matchArm
    : matchPattern ARROW (LBRACE statement* RBRACE | expression) COMMA?
    ;

matchPattern
    : UNDERSCORE                                     // wildcard
    | literal                                        // literal pattern
    | IDENTIFIER                                     // binding
    | IDENTIFIER DOUBLE_COLON anyIdentifier          // enum variant
    | IDENTIFIER DOUBLE_COLON anyIdentifier
        LPAREN matchPattern (COMMA matchPattern)* RPAREN  // enum variant with data
    ;

// Identyfikator rozszerzony — IDENTIFIER + builtin-type keywords:
// potrzebny w match patterns (Msg::Text) i import symbols (import stdlib { Int }).
anyIdentifier
    : IDENTIFIER
    | INT_TYPE | FLOAT_TYPE | BOOL_TYPE | TEXT_TYPE | SYMBOL_TYPE | TIME_TYPE | DURATION_TYPE
    | STORAGE | ENTITY | QUERY | FILTER | PROJECT | SORT | LIMIT | OFFSET | KEY | ASC | DESC
    | MUTATION | INSERT | UPDATE | DELETE | SINGLE | MANY | SET | WHERE
    ;

// ─────────────────── Communication statements ─────────────────────────

communicationStatement
    : sendStatement
    | receiveStatement
    | ackStatement
    | nackStatement
    | respondStatement
    ;

sendStatement
    : SEND expression (TO expression)? SEMICOLON?
    ;

receiveStatement
    : (LET | VAR) IDENTIFIER ASSIGN RECV FROM? expression SEMICOLON?
    ;

ackStatement
    : ACK expression SEMICOLON?
    ;

nackStatement
    : NACK expression (IDENTIFIER expression)? SEMICOLON?
    ;

respondStatement
    : RESPOND expression SEMICOLON?
    ;

// ─────────────────── Expressions ──────────────────────────────────────
// Precedence (ascending): assignment → conditional → or → and →
//   equality → relational → additive → multiplicative → cast →
//   unary → postfix → primary

expression
    : assignmentExpression
    ;

assignmentExpression
    : conditionalExpression (compoundOperator conditionalExpression)?
    ;

compoundOperator
    : ASSIGN
    | PLUS_ASSIGN | MINUS_ASSIGN | MULT_ASSIGN | DIV_ASSIGN | MOD_ASSIGN
    ;

conditionalExpression
    : logicalOrExpression (QUESTION expression COLON conditionalExpression)?
    ;

logicalOrExpression
    : logicalAndExpression (OR logicalAndExpression)*
    ;

logicalAndExpression
    : equalityExpression (AND equalityExpression)*
    ;

equalityExpression
    : relationalExpression ((EQ | NE) relationalExpression)*
    ;

relationalExpression
    : additiveExpression ((LT | GT | LE | GE) additiveExpression)*
    ;

additiveExpression
    : multiplicativeExpression ((PLUS | MINUS) multiplicativeExpression)*
    ;

multiplicativeExpression
    : castExpression ((MULTIPLY | DIVIDE | MODULO) castExpression)*
    ;

castExpression
    : unaryExpression (AS typeReference)?
    ;

unaryExpression
    : (MINUS | NOT | AMPERSAND) unaryExpression
    | postfixExpression
    ;

postfixExpression
    : primaryExpression
      ( LBRACKET expression RBRACKET         // index
      | DOT anyIdentifier                    // field access
      | DOUBLE_COLON anyIdentifier           // enum variant access
      | LPAREN argumentList? RPAREN          // function call
      )*
    ;

primaryExpression
    : literal
    | anyIdentifier
    | CONFIG                             // dostęp do bloku config: config.pole
    | LPAREN expression RPAREN
    | structLiteral
    | arrayLiteral
    | jsonLiteral
    | communicationExpression
    ;

communicationExpression
    : RECV FROM? expression           // receive from channel
    | SEND expression TO? expression  // send inline expression
    ;

argumentList
    : expression (COMMA expression)*
    | namedArgument (COMMA namedArgument)*
    ;

namedArgument
    : IDENTIFIER COLON expression
    ;

structLiteral
    : IDENTIFIER LBRACE structFieldInit (COMMA structFieldInit)* COMMA? RBRACE
    ;

structFieldInit
    : IDENTIFIER COLON expression
    ;

arrayLiteral
    : LBRACKET (expression (COMMA expression)* COMMA?)? RBRACKET
    ;

jsonLiteral
    : LBRACE (jsonField (COMMA jsonField)*)? RBRACE
    ;

jsonField
    : STRING_LITERAL COLON expression
    ;

// ─────────────────── Literals ─────────────────────────────────────────

literal
    : HEX_LITERAL
    | INTEGER_LITERAL
    | FLOAT_LITERAL
    | STRING_LITERAL
    | RAW_STRING
    | TRIPLE_STRING
    | DURATION_LITERAL
    | MEMORY_UNIT_LITERAL
    | TRUE
    | FALSE
    ;

// ═══════════════════════════════════════════════════════════════════
// LEXER RULES
// Kolejność ma znaczenie w ANTLR4:
// 1. Literały compound (duration/memory) PRZED INTEGER_LITERAL — priorytet matching
// 2. Słowa kluczowe PRZED IDENTIFIER
// 3. Dłuższe tokeny operatorowe PRZED krótszymi (np. == przed =)
// ═══════════════════════════════════════════════════════════════════

// ─────────────────── Keywords — Declarations ──────────────────────────

TASK            : 'task';
STRUCT          : 'struct';
ENUM            : 'enum';
FN              : 'fn';
PIPELINE        : 'pipeline';
DEVICE          : 'device';
NATIVE          : 'native';
CONFIG          : 'config';
IMPORT          : 'import';
NARRATIVE       : 'narrative';
DEPLOYMENT      : 'deployment';
SUPERVISOR      : 'supervisor';
CIRCUIT_BREAKER : 'circuit_breaker';
QUIRKS          : 'quirks';
STORAGE         : 'storage';
ENTITY          : 'entity';
QUERY           : 'query';
FILTER          : 'filter';
PROJECT         : 'project';
SORT            : 'sort';
LIMIT           : 'limit';
OFFSET          : 'offset';
KEY             : 'key';
ASC             : 'asc';
DESC            : 'desc';
MUTATION        : 'mutation';
INSERT          : 'insert';
UPDATE          : 'update';
DELETE          : 'delete';
SINGLE          : 'single';
MANY            : 'many';
SET             : 'set';
WHERE           : 'where';

// ─────────────────── Keywords — extern (D13) ───────────────────────────
// EXPERIMENTAL: FFI bridge

EXTERN      : 'extern';
TYPE        : 'type';

// ─────────────────── Keywords — Protocol (D7) ───────────────────────────

PROTOCOL    : 'protocol';

// ─────────────────── Keywords — Built-in types ────────────────────────

INT_TYPE      : 'Int';
FLOAT_TYPE    : 'Float';
BOOL_TYPE     : 'Bool';
TEXT_TYPE     : 'Text';
SYMBOL_TYPE   : 'Symbol';
TIME_TYPE     : 'Time';
DURATION_TYPE : 'Duration';
MAP_TYPE      : 'Map';       // D5: PascalCase
CHAN          : 'chan';
QUEUE         : 'queue';
STREAM        : 'stream';

// ─────────────────── Keywords — Communication ──────────────────────────

SEND        : 'send';
TO          : 'to';
RESPOND     : 'respond';
RECV        : 'recv';
FROM        : 'from';
ACK         : 'ack';
NACK        : 'nack';
BROADCAST   : 'broadcast';
PARALLEL    : 'parallel';

// ─────────────────── Keywords — Control flow ──────────────────────────

IF          : 'if';
ELSE        : 'else';
FOR         : 'for';
IN          : 'in';
MATCH       : 'match';
EVERY       : 'every';
LOOP        : 'loop';
RETURN      : 'return';
DELAY       : 'delay';
AS          : 'as';
LOG         : 'log';

// ─────────────────── Keywords — Variables & state ─────────────────────

LET         : 'let';
CONST       : 'const';
VAR         : 'var';
STATE       : 'state';
USE         : 'use';
ON          : 'on';

// ─────────────────── Keywords — Boolean ────────────────────────────────

TRUE        : 'true';
FALSE       : 'false';
AND         : 'and' | '&&';
OR          : 'or'  | '||';
NOT         : 'not' | '!';

// ─────────────────── Keywords — Platform targets ──────────────────────

EMBEDDED        : 'embedded';
SERVER          : 'server';
REPORT          : 'report';
EDGE            : 'edge';
CLOUD_SERVER    : 'cloud_server';
MOBILE          : 'mobile';
DESKTOP         : 'desktop';
WEB             : 'web';
MICROCONTROLLER : 'microcontroller';
FOG_NODE        : 'fog_node';
DATA_CENTER     : 'data_center';

// ─────────────────── Literals ─────────────────────────────────────────
// CRITICAL: Kolejność tokenów literałów — dłuższe/specyficzne PRZED krótszymi/ogólnymi:
// 1. HEX_LITERAL    (0xAB) PRZED INTEGER_LITERAL
// 2. DURATION_LITERAL (5s)  PRZED INTEGER_LITERAL
// 3. MEMORY_UNIT_LITERAL (4MB) PRZED INTEGER_LITERAL

HEX_LITERAL
    : '0' [xX] [0-9a-fA-F] ([0-9a-fA-F_]* [0-9a-fA-F])?
    ;

DURATION_LITERAL
    : [0-9]+ ('ns' | 'us' | 'ms' | 's' | 'm' | 'h' | 'd')
    ;

MEMORY_UNIT_LITERAL
    : [0-9]+ ('KB' | 'MB' | 'GB' | 'TB' | 'PB')
    ;

INTEGER_LITERAL
    : [0-9] ([0-9_]* [0-9])?
    ;

FLOAT_LITERAL
    : [0-9]+ '.' [0-9]+ ([eE] [+-]? [0-9]+)?
    ;

// Triple-quoted string — MUSI być przed STRING_LITERAL (dłuższy pattern wygrywa)
TRIPLE_STRING
    : '"""' .*? '"""'
    ;

STRING_LITERAL
    : '"' (~["\\\r\n] | '\\' .)*  '"'
    | '\'' (~['\\\r\n] | '\\' .)* '\''
    ;

// Raw string: @"..." z podwójnym cudzysłowem jako escape
RAW_STRING
    : '@"' (~'"' | '""')* '"'
    ;

// ─────────────────── Operators ────────────────────────────────────────
// Dłuższe tokeny PRZED krótszymi

PLUS_ASSIGN  : '+=';
MINUS_ASSIGN : '-=';
MULT_ASSIGN  : '*=';
DIV_ASSIGN   : '/=';
MOD_ASSIGN   : '%=';

DOUBLE_COLON : '::';
DOT_DOT      : '..';
ARROW        : '->';

EQ           : '==';
NE           : '!=';
LE           : '<=';
GE           : '>=';

ASSIGN       : '=';
PLUS         : '+';
MINUS        : '-';
MULTIPLY     : '*';
DIVIDE       : '/';
MODULO       : '%';
LT           : '<';
GT           : '>';
DOT          : '.';
COLON        : ':';
SEMICOLON    : ';';
COMMA        : ',';
LPAREN       : '(';
RPAREN       : ')';
LBRACE       : '{';
RBRACE       : '}';
LBRACKET     : '[';
RBRACKET     : ']';
ARROW_LEFT   : '<-';    // zarezerwowany token (NOT IN 0.2 parser rules)
AT           : '@';
HASH         : '#';
PIPE         : '|';
AMPERSAND    : '&';
QUESTION     : '?';
UNDERSCORE   : '_';

// ─────────────────── Identifier ────────────────────────────────────────

IDENTIFIER
    : [a-zA-Z_] [a-zA-Z0-9_]*
    ;

// ─────────────────── Whitespace & comments ────────────────────────────

WS
    : [ \t\r\n]+ -> skip
    ;

LINE_COMMENT
    : '//' ~[\r\n]* -> skip
    ;

BLOCK_COMMENT
    : '/*' .*? '*/' -> skip
    ;
