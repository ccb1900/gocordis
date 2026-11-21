package main

import (
	"flag"
	"fmt"
	"os"
)

func main() {
	dir := flag.String("dir", ".", "target project directory")
	module := flag.String("module", "", "Go module path, e.g. example.com/myapp")
	flag.Parse()
	if err := Generate(*dir, *module); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	fmt.Printf("created GoCordis project in %s\n", *dir)
}
