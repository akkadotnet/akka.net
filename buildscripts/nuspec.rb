require 'albacore/albacoretask'
require 'rexml/document'

class NuspecFile
  def initialize(src, target, exclude) 
    @src = src
    @target = target
    @exclude = exclude
  end
  
  def render(xml) 
    depend = xml.add_element 'file', { 'src' => @src }
    
    depend.add_attribute( 'target', @target ) unless @target.nil?
    depend.add_attribute( 'exclude', @exclude ) unless @exclude.nil?
  end
end

class NuspecDependency

  attr_accessor :id, :version

  def initialize(id, version)
    @id = id
    @version = version
  end
  
  def render( xml )
    depend = xml.add_element 'dependency', {'id' => @id, 'version' => @version}
  end
end

class NuspecDependencyGroup
  attr_accessor :target_framework

  def initialize(target_framework)
    @target_framework = target_framework
    @dependencies = []
  end

  def dependency(id, version)
    @dependencies.push NuspecDependency.new(id, version)
  end

  def render ( xml )
    if @dependencies.length > 0
      group = xml.add_element('group')
      group.add_attribute('targetFramework', @target_framework) unless @target_framework.nil?
      @dependencies.each {|x| x.render(group)}
    end
  end
end

class NuspecFrameworkAssembly

  attr_accessor :name, :target_framework

  def initialize(name, target_framework)
    @name = name
    @target_framework = target_framework
  end

  def render( xml )
    depend = xml.add_element 'frameworkAssembly', {'assemblyName' => @name, 'targetFramework' => @target_framework}
  end
end

class NuspecReference

  attr_accessor :file

  def initialize(file)
    @file = file
  end

  def render( xml )
    depend = xml.add_element 'reference', {'file' => @file}
  end
end

class Nuspec
  include Albacore::Task
  
  attr_accessor :id, :version, :title, :authors, :description, :language, :license_url, :project_url, :output_file,
                :owners, :summary, :icon_url, :require_license_acceptance, :tags, :working_directory, :copyright,
                :release_notes

  # Keep these around for backwards compatibility
  alias :licenseUrl :license_url
  alias :licenseUrl= :license_url=
  alias :projectUrl :project_url
  alias :projectUrl= :project_url=
  alias :iconUrl :icon_url
  alias :iconUrl= :icon_url=
  alias :requireLicenseAcceptance :require_license_acceptance
  alias :requireLicenseAcceptance= :require_license_acceptance=

  def initialize()
    @dependencies = Hash.new
    @files = []
    @frameworkAssemblies = []
    @references = []
    super()
  end

  attr_writer :pretty_formatting
  def pretty_formatting?
    @pretty_formatting
  end

  def dependency(id, version, target_framework = nil)
    #Lazily create the dependency groups
    if(!@dependencies.has_key?(target_framework))
      @dependencies[target_framework] = NuspecDependencyGroup.new(target_framework)
    end

    @dependencies[target_framework].dependency id, version
  end
  
  def file(src, target = nil, exclude = nil)
    @files.push NuspecFile.new(src, target, exclude)
  end

  def framework_assembly(name, target_framework)
    @frameworkAssemblies.push NuspecFrameworkAssembly.new(name, target_framework)
  end

  def reference (file)
    @references.push NuspecReference.new(file)
  end
  
  def execute
    check_required_field @output_file, "output_file"
    check_required_field @id, "id" 
    check_required_field @version, "version" 
    check_required_field @authors, "authors" 
    check_required_field @description, "description" 
    
    if(@working_directory.nil?)
      @working_output_file = @output_file
    else
      @working_output_file = File.join(@working_directory, @output_file)
    end

    builder = REXML::Document.new
    build(builder)
    output = ""
    builder.write(output, self.pretty_formatting? ? 2 : -1)

    @logger.debug "Writing #{@working_output_file}"

    File.open(@working_output_file, 'w') {|f| f.write(output) }
  end

  def build(document)
    document << REXML::XMLDecl.new

    package = document.add_element('package')
    package.add_attribute("xmlns", "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd")

    metadata = package.add_element('metadata')
    
    metadata.add_element('id').add_text(@id)
    metadata.add_element('version').add_text(@version)
    metadata.add_element('title').add_text(@title) unless @title.nil?
    metadata.add_element('authors').add_text(@authors)
    metadata.add_element('description').add_text(@description)
    metadata.add_element('releaseNotes').add_text(@release_notes)
    metadata.add_element('copyright').add_text(@copyright)
    metadata.add_element('language').add_text(@language) unless @language.nil?
    metadata.add_element('licenseUrl').add_text(@license_url) unless @license_url.nil?
    metadata.add_element('projectUrl').add_text(@project_url) unless @project_url.nil?
    metadata.add_element('owners').add_text(@owners) unless @owners.nil?
    metadata.add_element('summary').add_text(@summary) unless @summary.nil?
    metadata.add_element('iconUrl').add_text(@icon_url) unless @icon_url.nil?
    metadata.add_element('requireLicenseAcceptance').add_text(@require_license_acceptance) unless @require_license_acceptance.nil?
    metadata.add_element('tags').add_text(@tags) unless @tags.nil?

    if @dependencies.length > 0
      depend = metadata.add_element('dependencies')
      @dependencies.each_value {|x| x.render(depend)}
    end

    if @files.length > 0
      files = package.add_element('files')
      @files.each {|x| x.render(files)}
    end
    
    if @frameworkAssemblies.length > 0
      depend = metadata.add_element('frameworkAssemblies')
      @frameworkAssemblies.each {|x| x.render(depend)}
    end

    if @references.length > 0
      depend = metadata.add_element('references')
      @references.each {|x| x.render(depend)}
    end
  end

  def check_required_field(field, fieldname)
    raise "Nuspec: required field '#{fieldname}' is not defined" if field.nil?
    true
  end
end
