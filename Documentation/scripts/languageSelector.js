

function setActiveTab(baseClass,activeClassName,activeTab) {

  // set defalt for all tabs
  var classElements = getElementsByClass("CodeSnippetContainerTab", null, 'div');
  if(classElements != null) {
    for(i=0; i<classElements.length; i++) {       
 	  classElements[i].style.backgroundColor = "#dfedfe";
 	  classElements[i].style.borderTop = "1px solid #bbb";
 	  classElements[i].style.borderBottom = "2px solid #d0d2d2";
    }
  }
  var classElements = getElementsByClass("CodeSnippetContainerTabFirst", null, 'div');
  if(classElements != null) {
    for(i=0; i<classElements.length; i++) {       
 	  classElements[i].style.backgroundColor = "#dfedfe";
 	  classElements[i].style.borderTop = "1px solid #bbb";
 	  classElements[i].style.borderBottom = "2px solid #d0d2d2";
    }
  }
  
  // switch language
  var classElements = getElementsByClass(baseClass, null, 'div');
  if(classElements != null) {
    for(i=0; i<classElements.length; i++) {       
      var pattern = new RegExp("(^|\\s)"+activeClassName+"(\\s|$)");
 	  if ( pattern.test(classElements[i].className) ) {
        classElements[i].style.display = 'block';
	  }
	  else {
        classElements[i].style.display = 'none';
	  }
    }
  }
  // hightlight active tab
  var element = document.getElementById(activeTab);
  if(element != null)
  {
    element.style.backgroundColor = "white";
    element.style.borderBottomColor = "white";
  } 
}

function getElementsByClass(searchClass,node,tag) {
	var classElements = new Array();
	if ( node == null )
		node = document;
	if ( tag == null )
		tag = '*';
	var els = node.getElementsByTagName(tag);
	var elsLen = els.length;
	var pattern = new RegExp("(^|\\s)"+searchClass+"(\\s|$)");
	for (i = 0, j = 0; i < elsLen; i++) {
		if ( pattern.test(els[i].className) ) {
			classElements[j] = els[i];
			j++;
		}
	}
	return classElements;
}