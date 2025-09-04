#!/bin/bash

# Multi-Node Test Async Migration Status Checker
# This script helps identify which multi-node tests still need async migration

echo "========================================="
echo "Multi-Node Test Async Migration Status"
echo "========================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to check a directory
check_directory() {
    local dir=$1
    local name=$2
    
    echo -e "${YELLOW}Checking $name:${NC}"
    echo "----------------------------------------"
    
    # Find files with blocking TestConductor calls
    local blocking_files=$(find "$dir" -name "*.cs" -exec grep -l "TestConductor.*\.Wait()" {} \; 2>/dev/null | sort)
    
    if [ -z "$blocking_files" ]; then
        echo -e "${GREEN}✓ No TestConductor blocking calls found${NC}"
    else
        echo -e "${RED}✗ Files with TestConductor.*.Wait() calls:${NC}"
        for file in $blocking_files; do
            basename_file=$(basename "$file")
            count=$(grep -c "\.Wait()" "$file")
            echo "  - $basename_file ($count .Wait() calls)"
        done
    fi
    
    # Check for EnterBarrier (non-async)
    local barrier_count=$(find "$dir" -name "*.cs" -exec grep -l "EnterBarrier(" {} \; 2>/dev/null | wc -l)
    if [ "$barrier_count" -gt 0 ]; then
        echo -e "${YELLOW}⚠ $barrier_count files still use EnterBarrier (should be EnterBarrierAsync)${NC}"
    fi
    
    # Check for Within (non-async)
    local within_count=$(find "$dir" -name "*.cs" -exec grep -l "Within(" {} \; 2>/dev/null | wc -l)
    if [ "$within_count" -gt 0 ]; then
        echo -e "${YELLOW}⚠ $within_count files use Within (may need WithinAsync)${NC}"
    fi
    
    echo ""
}

# Check core tests
check_directory "src/core/Akka.Cluster.Tests.MultiNode" "Akka.Cluster.Tests.MultiNode"
check_directory "src/core/Akka.Remote.Tests.MultiNode" "Akka.Remote.Tests.MultiNode"

# Check contrib tests
if [ -d "src/contrib/cluster/Akka.Cluster.Sharding.Tests.MultiNode" ]; then
    check_directory "src/contrib/cluster/Akka.Cluster.Sharding.Tests.MultiNode" "Akka.Cluster.Sharding.Tests.MultiNode"
fi

if [ -d "src/contrib/cluster/Akka.Cluster.Tools.Tests.MultiNode" ]; then
    check_directory "src/contrib/cluster/Akka.Cluster.Tools.Tests.MultiNode" "Akka.Cluster.Tools.Tests.MultiNode"
fi

if [ -d "src/contrib/cluster/Akka.Cluster.Metrics.Tests.MultiNode" ]; then
    check_directory "src/contrib/cluster/Akka.Cluster.Metrics.Tests.MultiNode" "Akka.Cluster.Metrics.Tests.MultiNode"
fi

if [ -d "src/contrib/cluster/Akka.DistributedData.Tests.MultiNode" ]; then
    check_directory "src/contrib/cluster/Akka.DistributedData.Tests.MultiNode" "Akka.DistributedData.Tests.MultiNode"
fi

echo "========================================="
echo "Summary"
echo "========================================="

# Count total blocking files
total_blocking=$(find src -name "*.cs" -path "*Tests.MultiNode*" -exec grep -l "TestConductor.*\.Wait()" {} \; 2>/dev/null | wc -l)
total_files=$(find src -name "*.cs" -path "*Tests.MultiNode*" 2>/dev/null | wc -l)

echo "Total multi-node test files: $total_files"
echo -e "${RED}Files with blocking TestConductor calls: $total_blocking${NC}"

if [ "$total_blocking" -eq 0 ]; then
    echo -e "${GREEN}🎉 All TestConductor blocking calls have been migrated!${NC}"
else
    echo -e "${YELLOW}⚠ Migration still needed for $total_blocking files${NC}"
fi

echo ""
echo "Run this script periodically to track migration progress."
echo "See MULTINODE_TEST_ASYNC_MIGRATION.md for migration guide."