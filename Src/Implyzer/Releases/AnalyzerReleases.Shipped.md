## Release 1.0

### New Rules

 Rule ID | Category | Severity | Notes                                                      
---------|----------|----------|------------------------------------------------------------
 IMPL001 | Usage    | Error    | Implementation type mismatch (ReferenceType vs ValueType). 
 IMPL002 | Usage    | Error    | Implementation base type mismatch.                         

## Release 1.1

### New Rules

 Rule ID | Category       | Severity | Notes                                                      
---------|----------------|----------|------------------------------------------------------------
 IMPL001 | Implementation | Error    | Implementation type mismatch (ReferenceType vs ValueType). 
 IMPL002 | Implementation | Error    | Implementation base type mismatch.                         
 IMPL003 | Implementation | Error    | Indirect implementation required                           

## Release 1.3

### New Rules

 Rule ID | Category       | Severity | Notes                                          
---------|----------------|----------|------------------------------------------------
 IMPL004 | Implementation | Error    | Empty constructor not found in implementation. 

## Release 1.4

### New Rules

 Rule ID | Category | Severity | Notes                  
---------|----------|----------|------------------------
 IMPL005 | Design   | Info     | Use replacement symbol 

## Release 2026.5.2

### New Rules

 Rule ID | Category       | Severity | Notes                                          
---------|----------------|----------|------------------------------------------------
 IMPL006 | Design         | Error    | Interface must be partial                      
 IMPL007 | Design         | Error    | Target class must be partial                   
 IMPL008 | Design         | Error    | Target class must be a class                   
 IMPL009 | Implementation | Error    | Static method not implemented                  
 IMPL010 | Design         | Error    | Signature must be a delegate                   
 IMPL011 | Design         | Error    | Default implementation method not found        
 IMPL012 | Design         | Error    | Default implementation signature mismatch      
 IMPL013 | Design         | Error    | Default implementation method must be static   
 IMPL014 | Implementation | Error    | Registered type missing required static member 
 IMPL015 | Design         | Warning  | StaticRegister on non-static-abstract interface
 IMPL016 | Design         | Info     | Redundant static registration                  
 